using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        // ====================================================================
        // 2. QUÉT BOM CHO INTERFACE (DETAIL/HULL)
        // ====================================================================
        public List<BomHarvestRecord> HarvestInterfaceBom()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu HarvestInterfaceBom...");
            List<BomHarvestRecord> rawResults = new List<BomHarvestRecord>();
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor ed = doc.Editor;

                List<CatalogItem> masterCatalog = GetMasterCatalogItems();
                // IsPlanView/CountPlanViewOnly được edit ở Item Library ("Edit View Type"), không phải
                // Master Library — overlay giá trị từ project catalog đang active lên masterCatalog
                // (in-memory, không ghi lại MasterCatalog.json) trước khi dùng để resolve mainCatItem.
                OverlayViewTypeFromProjectCatalog(masterCatalog);

                PromptSelectionOptions selOpt = new PromptSelectionOptions();
                selOpt.MessageForAdding = "\nSelect A1 Frames to scan: ";
                TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT"), new TypedValue((int)DxfCode.BlockName, "`*U*,A1") };
                PromptSelectionResult selRes = ed.GetSelection(selOpt, new SelectionFilter(filter));
                
                if (selRes.Status != PromptStatus.OK) return rawResults;

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                        
                        List<BlockReference> allSpaceBlocks = new List<BlockReference>();
                        List<Entity> allSpaceGeometries = new List<Entity>();

                        foreach (ObjectId id in currentSpace)
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent is BlockReference b) allSpaceBlocks.Add(b);
                            else if (ent is Polyline || ent is Line || ent is Circle || ent is Arc) allSpaceGeometries.Add(ent);
                        }

                        int a1Counter = 1;
                        foreach (SelectedObject selObj in selRes.Value)
                        {
                            BlockReference a1Blk = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                            if (a1Blk == null || !a1Blk.Bounds.HasValue) continue;

                            string a1Name = FittingBlockUtility.GetEffectiveName(tr, a1Blk);
                            if (!a1Name.Equals("A1", StringComparison.OrdinalIgnoreCase)) continue;

                            Extents3d a1Ext = a1Blk.GeometricExtents;
                            string detailName = GetAttributeValue(tr, a1Blk, "VIEW_NAME");
                            if (string.IsNullOrEmpty(detailName)) detailName = GetAttributeValue(tr, a1Blk, "TITLE");
                            if (string.IsNullOrEmpty(detailName)) detailName = $"Hull {a1Counter}";

                            foreach (BlockReference innerBlk in allSpaceBlocks)
                            {
                                if (innerBlk.ObjectId == a1Blk.ObjectId) continue;
                                if (IsPointInsideExtents(innerBlk.Position, a1Ext))
                                    ExtractFittingsFromBlock(tr, innerBlk, detailName, 1, masterCatalog, rawResults);
                            }

                            ExtractGeometricItems(tr, allSpaceGeometries, a1Ext, detailName, masterCatalog, rawResults);
                            a1Counter++;
                        }
                        tr.Commit();
                    }
                }

                var consolidated = ConsolidateRawResults(rawResults);
                return ResolveMultiViewQuantity(consolidated);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI HarvestInterfaceBom: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Overlay <see cref="CatalogItem.IsPlanView"/>/<see cref="CatalogItem.CountPlanViewOnly"/> từ
        /// project catalog (Item Library) đang active lên <paramref name="masterCatalog"/> — theo
        /// BlockName. 2 field này được edit ở Item Library ("Edit View Type"), không phải Master
        /// Library, nên Master Catalog chỉ giữ giá trị mặc định lúc import; giá trị thật sự dùng để
        /// tính Qty phải lấy từ project catalog. In-memory only — không ghi lại MasterCatalog.json.
        /// </summary>
        private void OverlayViewTypeFromProjectCatalog(List<CatalogItem> masterCatalog)
        {
            var ctx = ActiveProjectContext.Instance;
            if (!ctx.HasActiveProject) return;

            try
            {
                var projectItems = CatalogJsonStore.Read<ProjectCatalogItem>(ctx.ProjectFilePath);
                var overlay = projectItems
                    .Where(p => !string.IsNullOrEmpty(p.BlockName))
                    .GroupBy(p => p.BlockName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var item in masterCatalog)
                {
                    if (string.IsNullOrEmpty(item.BlockName)) continue;
                    if (!overlay.TryGetValue(item.BlockName, out var projItem)) continue;
                    item.IsPlanView = projItem.IsPlanView;
                    item.CountPlanViewOnly = projItem.CountPlanViewOnly;
                }
                Debug.WriteLine($"{LOG_PREFIX} OverlayViewTypeFromProjectCatalog: overlaid {overlay.Count} project item(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} OverlayViewTypeFromProjectCatalog error: {ex.Message}");
            }
        }

        /// <summary>
        /// 1 fitting có thể xuất hiện qua NHIỀU view khác nhau (Front/Side/Section...) trong CÙNG 1
        /// khung A1 — mỗi view là 1 BlockName riêng nên <c>ConsolidateRawResults</c> KHÔNG gộp được
        /// (group theo <c>ParentBlockName</c>). Nếu cộng dồn Quantity của tất cả view lại thì sẽ đếm dư
        /// khi các view chỉ là nhiều cách minh hoạ cho CÙNG 1 vật lý (không phải nhiều fitting khác nhau).
        /// Quy tắc (theo quyết định user):
        ///   - Mặc định: lấy Quantity của view có số lượng LỚN NHẤT (MAX), không cộng dồn các view.
        ///   - Fitting được đánh dấu <see cref="CatalogItem.CountPlanViewOnly"/> (qua "Edit View Type" ở
        ///     Master Library — KHÔNG hardcode theo tên/Title, tường minh và mở rộng được cho fitting
        ///     khác trong tương lai): CHỈ lấy Quantity từ view được đánh dấu
        ///     <see cref="BomHarvestRecord.IsPlanView"/> (Plan/Top view) — bỏ qua toàn bộ view khác dù
        ///     Qty cao hơn. Nếu KHÔNG có view nào được đánh dấu Plan (block cũ import trước khi có tính
        ///     năng này, chưa có dữ liệu hướng camera) → fallback về quy tắc MAX chung.
        /// InstanceHandles của view bị loại vẫn được gộp vào record được chọn, để Sync Pos Num từ Item
        /// Library vẫn cập nhật đúng cho MỌI block minh hoạ, không chỉ view được dùng để tính Qty.
        /// </summary>
        private List<BomHarvestRecord> ResolveMultiViewQuantity(List<BomHarvestRecord> consolidated)
        {
            var result = new List<BomHarvestRecord>();

            var groups = consolidated.GroupBy(r => new { r.PanelName, r.VaultName, r.IsAccessory, r.ParentPartId });
            foreach (var group in groups)
            {
                var viewRecords = group.ToList();
                if (viewRecords.Count == 1)
                {
                    result.Add(viewRecords[0]);
                    continue;
                }

                bool countPlanViewOnly = viewRecords[0].CountPlanViewOnly;

                BomHarvestRecord chosen;
                if (countPlanViewOnly)
                {
                    var planViews = viewRecords.Where(r => r.IsPlanView).ToList();
                    chosen = planViews.Count > 0
                        ? planViews.OrderByDescending(r => r.Quantity).First()
                        : viewRecords.OrderByDescending(r => r.Quantity).First(); // fallback: chưa có dữ liệu Plan View
                }
                else
                {
                    chosen = viewRecords.OrderByDescending(r => r.Quantity).First();
                }

                chosen.InstanceHandles = viewRecords.SelectMany(r => r.InstanceHandles).Distinct().ToList();
                Debug.WriteLine($"{LOG_PREFIX} ResolveMultiViewQuantity: '{chosen.VaultName}' trong '{chosen.PanelName}' — {viewRecords.Count} view, chọn Qty={chosen.Quantity} (CountPlanViewOnly={countPlanViewOnly}).");
                result.Add(chosen);
            }

            return result;
        }

        private void ExtractFittingsFromBlock(Transaction tr, BlockReference blkRef, string detailName, int multiplier, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            string blkName = FittingBlockUtility.GetEffectiveName(tr, blkRef);
            if (blkName.Equals("A1", StringComparison.OrdinalIgnoreCase) || blkName.Equals("CAS_HEAD", StringComparison.OrdinalIgnoreCase)) return;

            // Opt-in: chỉ đếm khi BOM_TYPE khớp ĐÚNG DETAIL/HULL — không còn dựa vào tên block chứa
            // "CAS-" (naming convention dễ vỡ). Đối xứng với HarvestStructureBom (opt-in EQUIPMENT/PANEL).
            // Loại hẳn rủi ro double-count (1 block để trống BOM_TYPE mà vừa lồng trong Panel vừa nằm
            // trong khung A1) và sửa lỗ hổng ngược: block gắn đúng BOM_TYPE nhưng tên không có "CAS-"
            // trước đây bị loại khỏi CẢ 2 BOM.
            string bomType = GetAttributeValue(tr, blkRef, "BOM_TYPE").ToUpper();
            if (bomType == "DETAIL" || bomType == "HULL")
            {
                string vaultName = CleanVaultName(blkName);
                var mainCatItem = catalog.FirstOrDefault(c => c.PartNumber == vaultName || (c.BlockName != null && c.BlockName.Split(';').Contains(blkName, StringComparer.OrdinalIgnoreCase)));

                // C: dùng catalog PartNumber làm nguồn truth; fallback về vaultName nếu chưa có trong catalog
                string effectivePartId = mainCatItem?.PartNumber ?? vaultName;

                results.Add(new BomHarvestRecord {
                    PanelName = detailName, VaultName = effectivePartId, ParentBlockName = blkName,
                    Quantity = multiplier, UoM = "pcs", PartId = effectivePartId,
                    Description = mainCatItem != null && !string.IsNullOrEmpty(mainCatItem.Description) ? mainCatItem.Description : "Hull Fitting",
                    XClass = mainCatItem?.Title ?? "", ProjectPosNum = mainCatItem?.ProjectPosNum ?? "",
                    IsAccessory = false, ParentPartId = "", IsPlanView = mainCatItem?.IsPlanView ?? false,
                    CountPlanViewOnly = mainCatItem?.CountPlanViewOnly ?? false,
                    InstanceHandles = new List<long> { blkRef.ObjectId.Handle.Value }
                });

                if (mainCatItem != null && mainCatItem.Accessories != null && mainCatItem.Accessories.Count > 0)
                {
                    foreach (var acc in mainCatItem.Accessories)
                    {
                        var accCatItem = catalog.FirstOrDefault(c => c.PartNumber == acc.PartId);
                        results.Add(new BomHarvestRecord {
                            PanelName = detailName, VaultName = acc.PartId, ParentBlockName = blkName,
                            Quantity = acc.Quantity * multiplier, UoM = accCatItem?.UoM ?? "pcs", PartId = acc.PartId,
                            Description = accCatItem != null && !string.IsNullOrEmpty(accCatItem.Description) ? accCatItem.Description : "Accessory",
                            XClass = accCatItem?.Title ?? "Accessory", ProjectPosNum = accCatItem?.ProjectPosNum ?? "",
                            IsAccessory = true, ParentPartId = effectivePartId, IsPlanView = accCatItem?.IsPlanView ?? false,
                            CountPlanViewOnly = accCatItem?.CountPlanViewOnly ?? false,
                            InstanceHandles = new List<long> { blkRef.ObjectId.Handle.Value }
                        });
                    }
                }
            }

            if (!blkRef.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                foreach (ObjectId childId in btr)
                {
                    if (tr.GetObject(childId, OpenMode.ForRead) is BlockReference childBlk)
                    {
                        ExtractFittingsFromBlock(tr, childBlk, detailName, multiplier * 1, catalog, results);
                    }
                }
            }
        }

        private void ExtractGeometricItems(Transaction tr, List<Entity> geometries, Extents3d a1Ext, string detailName, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            var geoRules = catalog.Where(c => c.EntityType != "Block" && !string.IsNullOrEmpty(c.TriggerLayer)).ToList();
            if (geoRules.Count == 0) return;

            foreach (var ent in geometries)
            {
                Point3d centerPt = GetEntityCenter(ent);
                if (!IsPointInsideExtents(centerPt, a1Ext)) continue;

                string entType = ent.GetType().Name;
                string entLayer = ent.Layer;

                foreach (var rule in geoRules)
                {
                    if (rule.EntityType == entType && rule.TriggerLayer == entLayer)
                    {
                        double qty = 1.0;
                        if (rule.UoM == "m") 
                        {
                            if (ent is Polyline pl) qty = pl.Length / 1000.0;
                            else if (ent is Line ln) qty = ln.Length / 1000.0;
                            else if (ent is Arc arc) qty = arc.Length / 1000.0;
                        }

                        results.Add(new BomHarvestRecord {
                            PanelName = detailName, VaultName = rule.PartNumber, ParentBlockName = "Linear Item",
                            Quantity = (int)Math.Ceiling(qty), UoM = rule.UoM, PartId = rule.PartNumber,
                            Description = string.IsNullOrEmpty(rule.Description) ? "Virtual Item" : rule.Description,
                            XClass = rule.Title ?? "", ProjectPosNum = rule.ProjectPosNum ?? "",
                            IsAccessory = false, ParentPartId = "",
                            InstanceHandles = new List<long> { ent.ObjectId.Handle.Value }
                        });
                        break; 
                    }
                }
            }
        }
    }
}