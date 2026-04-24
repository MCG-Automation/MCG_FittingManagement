using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Drawing Collection — Phase 2 (UI thread): clone entity từ side db sang dest db,
    /// compute dx translation, unlock dest layers, scan existing A1 để nối tiếp offsetX.
    /// </summary>
    public partial class FittingManagementService
    {
        #region Phase 2 — clone vào current doc (UI thread)

        /// <summary>
        /// Clone toàn bộ modelspace của sideDb vào current space của dest db,
        /// sau đó dịch các entity cloned theo offsetX (chiều ngang) sao cho
        /// min X của bbox nằm đúng tại vị trí offsetX hiện tại.
        /// Trả về stats chi tiết (cloned/ignored/transformed).
        /// </summary>
        private CloneStats CloneToCurrentSpace(Database destDb, PreparedDrawing item, double offsetX)
        {
            var stats = new CloneStats();
            ObjectIdCollection srcIds = CollectModelSpaceIds(item.SideDb);
            stats.SrcIds = srcIds.Count;

            if (srcIds.Count == 0)
            {
                FileLogger.Log(LOG_PREFIX, $"  {item.FileName}: Model Space rỗng — bỏ qua clone.");
                return stats;
            }

            var mapping = new IdMapping();
            // WblockCloneObjects: gọi trên SOURCE db, clone sang BTR đích (current space) của DEST db.
            // DuplicateRecordCloning.Ignore → trùng tên layer/block: giữ bản của dest (bản vẽ gốc).
            item.SideDb.WblockCloneObjects(
                srcIds,
                destDb.CurrentSpaceId,
                mapping,
                DuplicateRecordCloning.Ignore,
                false);

            // Đếm breakdown IdMapping trước khi transform.
            foreach (IdPair pair in mapping)
            {
                if (pair.IsPrimary)
                {
                    if (pair.IsCloned) stats.PrimaryCloned++;
                    else stats.PrimaryNotCloned++;
                }
                else
                {
                    if (pair.IsCloned) stats.SymbolsCloned++;
                    else stats.SymbolsIgnored++;
                }
            }

            // D1 — Nếu có silent-drop, dump chi tiết IdMapping (first 30 pairs) để diagnose.
            // Log IsPrimary, IsCloned, source type, source name — cho thấy CHÍNH XÁC symbol nào
            // bị ignore và liệu renamed anon blocks có thực sự được xử lý như non-anon không.
            if (stats.PrimaryNotCloned > 0)
                LogIdMappingDetails(item.SideDb, mapping, maxPairs: 30);

            // Tính vector dịch: đưa minX bbox về đúng offsetX, giữ nguyên Y/Z.
            double dx = item.HasExtents ? (offsetX - item.Extents.MinPoint.X) : offsetX;
            stats.Dx = dx;
            var displace = Matrix3d.Displacement(new Vector3d(dx, 0, 0));

            using (var tr = destDb.TransactionManager.StartTransaction())
            {
                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsPrimary || !pair.IsCloned) continue;
                    if (pair.Value.IsNull) continue;

                    try
                    {
                        var obj = tr.GetObject(pair.Value, OpenMode.ForWrite, false, true);
                        if (obj is Entity ent)
                        {
                            ent.TransformBy(displace);
                            stats.Transformed++;

                            // Capture keep-as-is insertion: nếu cloned entity là BlockReference A1/CAS_HEAD,
                            // ghi vị trí+bbox sau TransformBy để overlap detection ở summary.
                            if (ent is BlockReference brDest)
                            {
                                string effName = GetEffectiveBlockName(brDest, tr);
                                if (effName != null && KeepAsIsBlocks.Contains(effName))
                                {
                                    try
                                    {
                                        var extDest = brDest.GeometricExtents;
                                        stats.KeepAsIsCloned.Add(new KeepAsIsClonedInfo
                                        {
                                            BlockName = effName,
                                            Position = brDest.Position,
                                            Rotation = brDest.Rotation,
                                            Bbox = extDest,
                                            DestHandleValue = brDest.Handle.Value
                                        });
                                    }
                                    catch { /* GeometricExtents có thể chưa sẵn sàng ngay sau TransformBy — bỏ qua */ }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        stats.TransformFailed++;
                        FileLogger.Log(LOG_PREFIX, $"TransformBy FAIL id={pair.Value}: {ex.Message}");
                    }
                }
                tr.Commit();
            }

            return stats;
        }

        /// <summary>
        /// Unlock + thaw mọi layer trong dest database. Tránh `eLayerLocked` / `eOnLockedLayer` khi
        /// WblockCloneObjects insert entity vào layer đang bị lock hoặc frozen.
        /// Trả số layer đã thay đổi (để log).
        /// </summary>
        private int UnlockAllLayersInDest(Database destDb)
        {
            int changed = 0;
            using (var tr = destDb.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(destDb.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    try
                    {
                        var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        bool needLockChange = ltr.IsLocked;
                        bool needFrozenChange = ltr.IsFrozen && ltr.Name != "0"; // layer "0" không thể thaw khi là current
                        if (!needLockChange && !needFrozenChange) continue;

                        ltr.UpgradeOpen();
                        if (needLockChange) ltr.IsLocked = false;
                        if (needFrozenChange)
                        {
                            try { ltr.IsFrozen = false; }
                            catch { /* layer hiện hành không thể thaw — bỏ qua */ }
                        }
                        changed++;
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Log(LOG_PREFIX, $"Unlock layer FAIL (id={id}): {ex.Message}");
                    }
                }
                tr.Commit();
            }
            return changed;
        }

        /// <summary>
        /// Scan Model Space của DEST doc tìm Xmax của mọi BlockReference A1/CAS_HEAD hiện hữu.
        /// Dùng để set initial offsetX = Xmax + GAP → collect mới sẽ nối tiếp sau A1 cũ, không đè lên.
        /// Nếu dest chưa có A1/CAS_HEAD nào → trả 0 (layout bắt đầu từ gốc).
        /// </summary>
        private double ComputeInitialOffsetX(Database destDb)
        {
            double maxX = double.NegativeInfinity;
            int existingCount = 0;

            using (var tr = destDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(BlockTableRecord.ModelSpace)) { tr.Commit(); return 0; }

                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    string effName;
                    try { effName = GetEffectiveBlockName(br, tr); }
                    catch { continue; }
                    if (effName == null || !KeepAsIsBlocks.Contains(effName)) continue;

                    try
                    {
                        var ext = br.GeometricExtents;
                        if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                        existingCount++;
                    }
                    catch { /* reference không có extents — bỏ qua trong tính Xmax */ }
                }
                tr.Commit();
            }

            if (existingCount == 0)
            {
                FileLogger.Log(LOG_PREFIX,
                    "Scan dest ModelSpace: không có A1/CAS_HEAD existing → initial offsetX = 0 (bắt đầu từ gốc).");
                return 0;
            }

            double initial = maxX + COLLECTION_GAP;
            FileLogger.Log(LOG_PREFIX,
                $"Scan dest ModelSpace: {existingCount} A1/CAS_HEAD reference(s) existing, Xmax={maxX:F0} " +
                $"→ initial offsetX = {initial:F0} (collect mới nối tiếp, cách {COLLECTION_GAP:F0}mm).");
            return initial;
        }

        /// <summary>
        /// D1 — Dump chi tiết IdMapping sau WblockCloneObjects để diagnose silent-drop.
        /// Log first N pairs với: IsPrimary, IsCloned, source type, source name (nếu là symbol table record
        /// hoặc BlockReference), source handle. Giúp thấy CHÍNH XÁC symbol nào bị ignore và tại sao.
        /// </summary>
        private static void LogIdMappingDetails(Database sideDb, IdMapping mapping, int maxPairs)
        {
            // Count total pairs first (without opening transaction).
            int total = 0;
            foreach (IdPair _ in mapping) total++;

            FileLogger.Log(LOG_PREFIX, $"  [D1] IdMapping dump (total={total}, showing first {Math.Min(maxPairs, total)}):");

            int idx = 0;
            using (var tr = sideDb.TransactionManager.StartTransaction())
            {
                foreach (IdPair pair in mapping)
                {
                    if (idx >= maxPairs) break;
                    idx++;

                    string srcType = "?";
                    string srcDesc = "";
                    try
                    {
                        if (!pair.Key.IsNull)
                        {
                            var obj = tr.GetObject(pair.Key, OpenMode.ForRead, false, true);
                            srcType = obj.GetType().Name;

                            if (obj is BlockTableRecord btr)
                                srcDesc = $"'{btr.Name}' IsAnon={btr.IsAnonymous}";
                            else if (obj is LayerTableRecord ltr)
                                srcDesc = $"'{ltr.Name}'";
                            else if (obj is LinetypeTableRecord lttr)
                                srcDesc = $"'{lttr.Name}'";
                            else if (obj is TextStyleTableRecord tstr)
                                srcDesc = $"'{tstr.Name}'";
                            else if (obj is DimStyleTableRecord dstr)
                                srcDesc = $"'{dstr.Name}'";
                            else if (obj is RegAppTableRecord rat)
                                srcDesc = $"'{rat.Name}'";
                            else if (obj is BlockReference br)
                            {
                                try
                                {
                                    var refBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                                    srcDesc = $"→BTR'{refBtr.Name}' IsAnon={refBtr.IsAnonymous}";
                                }
                                catch { srcDesc = "→BTR(?)"; }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        srcType = $"?(err:{ex.GetType().Name})";
                    }

                    string destHandle = pair.Value.IsNull
                        ? "null"
                        : $"h={pair.Value.Handle}";
                    string srcHandle = pair.Key.IsNull ? "null" : $"h={pair.Key.Handle}";

                    FileLogger.Log(LOG_PREFIX,
                        $"    [{idx,2}] Prim={pair.IsPrimary,-5} Cloned={pair.IsCloned,-5} " +
                        $"{srcType,-22} {srcDesc} (src {srcHandle}, dest {destHandle})");
                }
                tr.Commit();
            }

            if (total > maxPairs)
                FileLogger.Log(LOG_PREFIX, $"    ... và {total - maxPairs} pair(s) khác (tăng maxPairs nếu cần full).");
        }

        private ObjectIdCollection CollectModelSpaceIds(Database sideDb)
        {
            var ids = new ObjectIdCollection();
            using (var tr = sideDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(BlockTableRecord.ModelSpace)) { tr.Commit(); return ids; }
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms) ids.Add(id);
                tr.Commit();
            }
            return ids;
        }

        #endregion
    }
}
