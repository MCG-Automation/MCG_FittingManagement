using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;
using MCGCadPlugin.Utilities.FittingManagement;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// PHASE 2 của luồng Import IDW — tách từng drawing view trong DWG (đã export từ Inventor)
    /// thành 1 BlockTableRecord riêng, map layer theo kiểu nét của Inventor (visible/hidden/center),
    /// inject 10 attribute chuẩn, wblock ra file .dwg riêng trong Central Library, đăng ký MasterCatalog.
    /// </summary>
    public partial class FittingManagementService
    {
        // Tolerance khi clip entity theo bbox view (mm) — để bắt trọn edge entities vẽ sát biên
        private const double BBOX_TOLERANCE_MM = 50.0;

        // Layer đích cho kiểu nét trong block (theo chuẩn Mechanical-AM)
        private const string LAYER_VISIBLE = "0";
        private const string LAYER_HIDDEN = "Mechanical-AM_3";
        private const string LAYER_CENTER = "Mechanical-AM_7";
        private const string LAYER_LABEL = "Mechanical-AM_9";
        private const short COLOR_HIDDEN = 6;   // Magenta — hidden lines
        private const short COLOR_CENTER = 4;   // Cyan — center lines
        private const short COLOR_LABEL = 7;    // White — labels (reserved, không dùng trong Phase 1+2)

        /// <summary>
        /// PHASE 2 — Với mỗi file đã extract: mở DWG, split từng drawing view thành block riêng
        /// trong bản vẽ AutoCAD hiện tại. Sau khi xong, gọi PublishToCentralLibrary để wblock
        /// mỗi block ra file .dwg trong thư viện và cập nhật MasterCatalog.
        /// </summary>
        private void CreateBlocksFromExtracted(List<ExtractedIdw> items, string bomType, ImportResult result, IProgress<string> progress)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                FileLogger.Log(LOG_PREFIX, "CẢNH BÁO: Không có bản vẽ AutoCAD đang mở — bỏ qua PHASE 2.");
                foreach (var it in items)
                    result.AddError(it.SourceIdwName, "Không có bản vẽ AutoCAD đang mở để tạo block.");
                result.FailCount += items.Count;
                return;
            }

            Database db = doc.Database;
            var publishQueue = new List<Tuple<ObjectId, CatalogItem>>();
            int total = items.Count;

            using (doc.LockDocument())
            {
                for (int i = 0; i < total; i++)
                {
                    var item = items[i];
                    progress?.Report($"[{i + 1}/{total}] Creating blocks: {item.SourceIdwName}");
                    try
                    {
                        var tuples = ImportSingleDwgWithSplit(db, item, bomType);
                        if (tuples.Count > 0)
                        {
                            publishQueue.AddRange(tuples);
                            result.SuccessCount++;
                            FileLogger.Log(LOG_PREFIX,
                                $"Import {item.SourceIdwName} THÀNH CÔNG — Tạo {tuples.Count} block(s).");
                        }
                        else
                        {
                            result.FailCount++;
                            result.AddError(item.SourceIdwName,
                                "Không tạo được block (không có view hoặc bbox không chứa entity hợp lệ).");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        result.FailCount++;
                        FileLogger.LogException(LOG_PREFIX, $"split-view '{item.SourceIdwName}'", ex);
                        Debug.WriteLine($"{LOG_PREFIX} LỖI split-view '{item.SourceIdwName}': {ex.Message}");
                        result.AddError(item.SourceIdwName, $"Split: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // PHASE 2.5 — Publish block ra thư viện (.dwg riêng) + cập nhật MasterCatalog
            if (publishQueue.Count > 0)
            {
                progress?.Report($"Publishing {publishQueue.Count} block(s) to library...");
                try
                {
                    var merge = PublishToCentralLibrary(publishQueue);
                    FileLogger.Log(LOG_PREFIX,
                        $"Publish xong — MasterCatalog: Mới={merge.Item1}, Cập nhật={merge.Item2}.");
                }
                catch (System.Exception ex)
                {
                    FileLogger.LogException(LOG_PREFIX, "PublishToCentralLibrary", ex);
                    // Block đã có trong bản vẽ — publish fail không rollback success count.
                }
            }
        }

        /// <summary>
        /// Đọc DWG vào sideDb, dùng <c>WblockCloneObjects</c> clone cross-db trực tiếp
        /// các entity hợp lệ (Line/Arc/Circle/Polyline/Spline/Ellipse) rơi trong bbox từng view
        /// sang BTR đích. Sau đó dịch tâm view về gốc toạ độ + remap layer theo kiểu nét + inject attribute.
        /// </summary>
        private List<Tuple<ObjectId, CatalogItem>> ImportSingleDwgWithSplit(
            Database db, ExtractedIdw extracted, string bomType)
        {
            var results = new List<Tuple<ObjectId, CatalogItem>>();
            string dwgPath = extracted.DwgPath;
            FittingMetadata metadata = extracted.Metadata;

            if (!File.Exists(dwgPath))
            {
                FileLogger.Log(LOG_PREFIX, $"  CẢNH BÁO: DWG không tồn tại: {dwgPath}");
                return results;
            }

            if (metadata?.Views == null || metadata.Views.Count == 0)
            {
                FileLogger.Log(LOG_PREFIX, "  CẢNH BÁO: Metadata không có drawing view — bỏ qua file này.");
                return results;
            }

            string baseFileName = Path.GetFileNameWithoutExtension(dwgPath);

            int skipped3D = 0;
            using (var sourceDb = new Database(false, true))
            {
                sourceDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");

                using (var destTr = db.TransactionManager.StartTransaction())
                using (var srcTr = sourceDb.TransactionManager.StartTransaction())
                {
                    // Unlock + create 4 layer đích
                    UnlockLayerIfExists(db, destTr, LAYER_VISIBLE);
                    UnlockLayerIfExists(db, destTr, LAYER_HIDDEN);
                    UnlockLayerIfExists(db, destTr, LAYER_CENTER);
                    UnlockLayerIfExists(db, destTr, LAYER_LABEL);
                    FittingBlockUtility.CheckAndCreateLayer(db, destTr, LAYER_HIDDEN, COLOR_HIDDEN);
                    FittingBlockUtility.CheckAndCreateLayer(db, destTr, LAYER_CENTER, COLOR_CENTER);
                    FittingBlockUtility.CheckAndCreateLayer(db, destTr, LAYER_LABEL, COLOR_LABEL);

                    BlockTable destBt = (BlockTable)destTr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    BlockTableRecord srcMs = (BlockTableRecord)srcTr.GetObject(
                        SymbolUtilityServices.GetBlockModelSpaceId(sourceDb), OpenMode.ForRead);

                    // Pre-compute allowed entities + center — 1 pass/DWG (tránh GeometricExtents lặp V×E).
                    // Đồng thời enumerate TẤT CẢ entity types (kept + filtered + no-extents) để log breakdown
                    // cho user biết DWG chứa gì và ta đang drop cái nào.
                    var allowed = new List<(ObjectId Id, double Cx, double Cy)>();
                    var keptBreakdown = new Dictionary<string, int>();
                    var filteredBreakdown = new Dictionary<string, int>();
                    var noExtentsBreakdown = new Dictionary<string, int>();
                    int totalEntities = 0;

                    foreach (ObjectId eid in srcMs)
                    {
                        Entity ent = srcTr.GetObject(eid, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        totalEntities++;

                        string typeName = ent.GetType().Name;

                        if (!IsAllowedEntityType(ent))
                        {
                            IncrementCount(filteredBreakdown, typeName);
                            continue;
                        }

                        try
                        {
                            Extents3d ext = ent.GeometricExtents;
                            double cx = (ext.MinPoint.X + ext.MaxPoint.X) * 0.5;
                            double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5;
                            allowed.Add((eid, cx, cy));
                            IncrementCount(keptBreakdown, typeName);
                        }
                        catch
                        {
                            // Entity dạng allowed nhưng không lấy được extents (vd empty polyline) → không contribute vào view bbox.
                            IncrementCount(noExtentsBreakdown, typeName);
                        }
                    }

                    LogEntityBreakdown(totalEntities, keptBreakdown, filteredBreakdown, noExtentsBreakdown);

                    foreach (var view in metadata.Views)
                    {
                        if (view == null) continue;

                        // Yêu cầu: chỉ block hoá view 2D ortho. View 3D iso/arbitrary skip — không vào Master Library.
                        if (view.Is3D)
                        {
                            skipped3D++;
                            FileLogger.Log(LOG_PREFIX, $"  Bỏ qua '{view.Name}' — view 3D (iso/arbitrary), không publish vào Master.");
                            continue;
                        }

                        double minX = view.CenterX - view.Width * 0.5 - BBOX_TOLERANCE_MM;
                        double maxX = view.CenterX + view.Width * 0.5 + BBOX_TOLERANCE_MM;
                        double minY = view.CenterY - view.Height * 0.5 - BBOX_TOLERANCE_MM;
                        double maxY = view.CenterY + view.Height * 0.5 + BBOX_TOLERANCE_MM;

                        var entsToClone = new ObjectIdCollection();
                        foreach (var a in allowed)
                        {
                            if (a.Cx >= minX && a.Cx <= maxX && a.Cy >= minY && a.Cy <= maxY)
                                entsToClone.Add(a.Id);
                        }

                        if (entsToClone.Count == 0)
                        {
                            FileLogger.Log(LOG_PREFIX,
                                $"  Bỏ qua '{view.Name}' — không có entity nào rơi trong bbox.");
                            continue;
                        }

                        // Tạo BTR đích với tên duy nhất
                        string baseName = $"{baseFileName}_{view.Name}";
                        string uniqueName = GenerateUniqueBlockName(destBt, baseName);
                        var newBtr = new BlockTableRecord
                        {
                            Name = uniqueName,
                            Origin = Point3d.Origin
                        };
                        destBt.Add(newBtr);
                        destTr.AddNewlyCreatedDBObject(newBtr, true);

                        // Clone từng entity riêng — tránh eHandleExists khi WblockCloneObjects gặp
                        // handle collision cross-db (handle gốc từ Inventor DWG trùng với active drawing).
                        // AppendEntity luôn gán handle mới trong destination DB.
                        foreach (ObjectId eid in entsToClone)
                        {
                            Entity srcEnt = srcTr.GetObject(eid, OpenMode.ForRead) as Entity;
                            if (srcEnt == null) continue;
                            Entity cloned = srcEnt.Clone() as Entity;
                            if (cloned == null) continue;
                            newBtr.AppendEntity(cloned);
                            destTr.AddNewlyCreatedDBObject(cloned, true);
                        }

                        // Translate về origin + remap layer theo kiểu nét + force ByLayer
                        TransformAndStyleEntities(newBtr, destTr,
                            new Vector3d(-view.CenterX, -view.CenterY, 0));

                        // Inject 10 attribute chuẩn (7 từ metadata + BOM_TYPE + POS_NUM + VIEW_NAME)
                        InjectBimAttributes(newBtr, destTr, metadata, bomType, view.Name);

                        string exportPath = Path.Combine(_libraryFolderPath, uniqueName + ".dwg");
                        var catalogItem = new CatalogItem
                        {
                            BlockName = uniqueName,
                            PartNumber = metadata.PartNumber ?? "",
                            Description = metadata.Description ?? "",
                            Material = metadata.Material ?? "",
                            Mass = metadata.Mass ?? "",
                            Revision = metadata.Revision ?? "",
                            Designer = metadata.Designer ?? "",
                            Title = metadata.Title ?? "",
                            BomType = bomType,
                            FilePath = exportPath,
                            EntityType = "Block",
                            UoM = "pcs"
                        };
                        results.Add(Tuple.Create(newBtr.ObjectId, catalogItem));
                        FileLogger.Log(LOG_PREFIX,
                            $"  Block '{uniqueName}' tạo xong: {entsToClone.Count} entity clone.");
                    }

                    destTr.Commit();
                    // srcTr là read-only — dispose là đủ
                }

                FileLogger.Log(LOG_PREFIX,
                    $"  '{Path.GetFileName(dwgPath)}' summary — Tạo {results.Count} block 2D, bỏ qua {skipped3D} view 3D.");
            }

            return results;
        }

        #region Helpers — Split View

        /// <summary>
        /// Chỉ chấp nhận geometry "sạch" cho fitting block — bỏ text/dim/hatch/blockref từ DWG Inventor.
        /// </summary>
        private static bool IsAllowedEntityType(Entity ent)
        {
            return ent is Line
                || ent is Arc
                || ent is Circle
                || ent is Polyline
                || ent is Polyline2d
                || ent is Polyline3d
                || ent is Spline
                || ent is Ellipse;
        }

        /// <summary>Tăng counter cho entity type name (tránh TryGetValue pattern ở call site).</summary>
        private static void IncrementCount(Dictionary<string, int> dict, string key)
        {
            if (dict.TryGetValue(key, out int v)) dict[key] = v + 1;
            else dict[key] = 1;
        }

        /// <summary>
        /// Log chi tiết entity breakdown của DWG — dùng để user + dev phân tích data DWG và
        /// quyết định có cần mở rộng allow-list IsAllowedEntityType không.
        /// Section [KEEP] / [SKIP] / [NO EXTENTS] sort theo count DESC.
        /// </summary>
        private static void LogEntityBreakdown(int total,
            Dictionary<string, int> kept,
            Dictionary<string, int> filtered,
            Dictionary<string, int> noExtents)
        {
            int keptTotal = 0; foreach (var v in kept.Values) keptTotal += v;
            int filteredTotal = 0; foreach (var v in filtered.Values) filteredTotal += v;
            int noExtTotal = 0; foreach (var v in noExtents.Values) noExtTotal += v;

            FileLogger.Log(LOG_PREFIX,
                $"  DWG entity breakdown — total={total}, kept={keptTotal}, filtered={filteredTotal}, no-extents={noExtTotal}.");

            if (kept.Count > 0)
            {
                FileLogger.Log(LOG_PREFIX, "    [KEEP] (included vào view block):");
                foreach (var kv in kept.OrderByDescending(x => x.Value))
                    FileLogger.Log(LOG_PREFIX, $"      • {kv.Key}: {kv.Value}");
            }

            if (filtered.Count > 0)
            {
                FileLogger.Log(LOG_PREFIX,
                    "    [SKIP] (KHÔNG copy vào view block — text/dim/hatch/blockref thường từ title frame hoặc annotation):");
                foreach (var kv in filtered.OrderByDescending(x => x.Value))
                    FileLogger.Log(LOG_PREFIX, $"      • {kv.Key}: {kv.Value}");
                FileLogger.Log(LOG_PREFIX,
                    "    → Nếu type lạ/quan trọng xuất hiện ở đây, update IsAllowedEntityType trong JsonImport.cs.");
            }

            if (noExtents.Count > 0)
            {
                FileLogger.Log(LOG_PREFIX,
                    "    [NO EXTENTS] (type allowed nhưng GeometricExtents throw — thường entity rỗng/degenerate):");
                foreach (var kv in noExtents.OrderByDescending(x => x.Value))
                    FileLogger.Log(LOG_PREFIX, $"      • {kv.Key}: {kv.Value}");
            }
        }

        /// <summary>
        /// Dịch toàn bộ entity trong BTR về tâm gốc, remap layer theo tên kiểu nét của Inventor,
        /// và force <c>ColorIndex=256 (ByLayer)</c>, Linetype/LineWeight=ByLayer để layer rules áp dụng đúng.
        /// </summary>
        private static void TransformAndStyleEntities(BlockTableRecord btr, Transaction tr, Vector3d moveVec)
        {
            Matrix3d xform = Matrix3d.Displacement(moveVec);
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) continue;

                ent.TransformBy(xform);

                // Remap layer theo tên kiểu nét Inventor exports
                string ly = (ent.Layer ?? "").ToUpper();
                if (ly.Contains("VISIBLE")) ent.Layer = LAYER_VISIBLE;
                else if (ly.Contains("HIDDEN")) ent.Layer = LAYER_HIDDEN;
                else if (ly.Contains("CENTER")) ent.Layer = LAYER_CENTER;
                // Layer không match → về "0" (layer gốc Inventor không tồn tại trong destination DB)
                else ent.Layer = LAYER_VISIBLE;

                // Force ByLayer cho mọi property hiển thị — layer rules sẽ quyết định màu/nét/weight
                try
                {
                    ent.ColorIndex = 256; // 256 = ByLayer
                    ent.Linetype = "ByLayer";
                    ent.LineWeight = LineWeight.ByLayer;
                }
                catch
                {
                    // Một số entity type (ví dụ Viewport) không cho set — bỏ qua an toàn
                }
            }
        }

        /// <summary>
        /// Cấy 10 attribute (invisible) vào BTR:
        /// PART_NUMBER, DESCRIPTION, MATERIAL, MASS, REVISION, DESIGNER, TITLE, BOM_TYPE, POS_NUM, VIEW_NAME.
        /// </summary>
        private static void InjectBimAttributes(BlockTableRecord btr, Transaction tr,
            FittingMetadata meta, string bomType, string viewName)
        {
            FittingBlockUtility.AddAttributeDef(btr, tr, "PART_NUMBER", meta?.PartNumber ?? "", "PN", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "DESCRIPTION", meta?.Description ?? "", "DESC", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "MATERIAL", meta?.Material ?? "", "MAT", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "MASS", meta?.Mass ?? "", "MASS", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "REVISION", meta?.Revision ?? "", "REV", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "DESIGNER", meta?.Designer ?? "", "DESIGNER", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "TITLE", meta?.Title ?? "", "TITLE", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "BOM_TYPE", (bomType ?? "").ToUpper(), "BOM_TYPE", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "POS_NUM", "", "POS_NUM", true);
            FittingBlockUtility.AddAttributeDef(btr, tr, "VIEW_NAME", viewName ?? "", "VIEW_NAME", true);
        }

        /// <summary>
        /// Sinh tên block không trùng — append _1, _2... nếu đã tồn tại trong BlockTable.
        /// </summary>
        private static string GenerateUniqueBlockName(BlockTable bt, string baseName)
        {
            int suffix = 1;
            string name = baseName;
            while (bt.Has(name)) name = $"{baseName}_{suffix++}";
            return name;
        }

        /// <summary>
        /// Mở khoá layer nếu tồn tại và đang lock. No-op nếu layer chưa có.
        /// </summary>
        private static void UnlockLayerIfExists(Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName)) return;
            LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            if (ltr.IsLocked) ltr.IsLocked = false;
        }

        #endregion
    }
}
