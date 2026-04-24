using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Drawing Collection — Phase 1 (worker thread): đọc side db, rename block,
    /// purge unused, tính bbox, scan keep-as-is references.
    /// </summary>
    public partial class FittingManagementService
    {
        #region Phase 1 — preprocess side db (worker thread)

        /// <summary>Mỗi file: đọc side db, rename, purge, đo bbox. File lỗi → ghi vào result và bỏ qua.</summary>
        private List<PreparedDrawing> PreprocessAll(string[] paths, IProgress<string> progress, ImportResult result, string destDocPath)
        {
            var list = new List<PreparedDrawing>(paths.Length);

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string fileName = Path.GetFileName(path);
                progress?.Report($"[{i + 1}/{paths.Length}] Đang xử lý: {fileName}");

                if (!File.Exists(path))
                {
                    result.FailCount++;
                    result.AddError(fileName, "File không tồn tại.");
                    FileLogger.Log(LOG_PREFIX, $"SKIP — không tồn tại: {path}");
                    continue;
                }

                // Self-collect guard: nếu user lỡ chọn chính file nhà kho trong OpenFileDialog,
                // đọc side db từ disk (state cũ, không có unsaved changes) rồi clone vào chính doc
                // đang mở → content bị nhân đôi, block bị rename prefix, phá cấu trúc hiện tại.
                if (destDocPath != null)
                {
                    try
                    {
                        string selectedFullPath = Path.GetFullPath(path);
                        if (string.Equals(selectedFullPath, destDocPath, StringComparison.OrdinalIgnoreCase))
                        {
                            result.FailCount++;
                            result.AddError(fileName,
                                "Không thể collect chính bản vẽ 'nhà kho' đang mở vào chính nó. " +
                                "Lưu file và mở bản vẽ khác làm nhà kho, hoặc bỏ file này khỏi danh sách chọn.");
                            FileLogger.Log(LOG_PREFIX,
                                $"SKIP — self-collect detected (file trùng dest doc): {path}");
                            continue;
                        }
                    }
                    catch { /* path resolve fail — coi như không trùng, để logic chính handle */ }
                }

                Database sideDb = null;
                var fileSw = Stopwatch.StartNew();
                try
                {
                    sideDb = new Database(false, true);
                    sideDb.ReadDwgFile(path, FileShare.Read, allowCPConversion: true, password: null);
                    sideDb.CloseInput(true);
                    long readMs = fileSw.ElapsedMilliseconds;

                    string prefix = SanitizeBlockNamePart(Path.GetFileNameWithoutExtension(path));
                    var renameStats = RenameBlocksInSideDb(sideDb, prefix);
                    // P3 — pre-rename anonymous blocks được reference từ ModelSpace.
                    // Tránh silent-drop ở Phase 2 khi dest template có anon blocks trùng tên.
                    RenameReferencedAnonBlocksInSideDb(sideDb, prefix, renameStats);
                    long renameMs = fileSw.ElapsedMilliseconds - readMs;

                    var purgeStats = PurgeUnusedInSideDb(sideDb);
                    long purgeMs = fileSw.ElapsedMilliseconds - readMs - renameMs;

                    var extStats = ComputeModelSpaceExtents(sideDb);
                    long extMs = fileSw.ElapsedMilliseconds - readMs - renameMs - purgeMs;
                    fileSw.Stop();

                    LogPreparedFile(fileName, renameStats, purgeStats, extStats,
                        readMs, renameMs, purgeMs, extMs, fileSw.ElapsedMilliseconds);

                    list.Add(new PreparedDrawing
                    {
                        FileName = fileName,
                        SideDb = sideDb,
                        HasExtents = extStats.HasExtents,
                        Width = extStats.Width,
                        Extents = extStats.Extents,
                        RenameStats = renameStats,
                        PurgeStats = purgeStats,
                        ExtStats = extStats
                    });
                }
                catch (Autodesk.AutoCAD.Runtime.Exception acEx) when (IsRecoverableCorruptError(acEx))
                {
                    // File DWG cần chạy RECOVER trong AutoCAD trước.
                    result.FailCount++;
                    result.AddError(fileName,
                        $"File DWG bị lỗi cần chạy lệnh RECOVER trong AutoCAD trước khi collect ({acEx.ErrorStatus}).");
                    FileLogger.Log(LOG_PREFIX,
                        $"SKIP — DWG cần RECOVER: {fileName} (ErrorStatus={acEx.ErrorStatus})");
                    sideDb?.Dispose();
                }
                catch (Exception ex)
                {
                    result.FailCount++;
                    result.AddError(fileName, ex.Message);
                    FileLogger.LogException(LOG_PREFIX, $"Preprocess {fileName}", ex);
                    sideDb?.Dispose();
                }
            }

            return list;
        }

        private void LogPreparedFile(string fileName, RenameStats r, PurgeStats p, ExtentsStats e,
            long readMs, long renameMs, long purgeMs, long extMs, long totalMs)
        {
            FileLogger.Log(LOG_PREFIX, $"Prepared '{fileName}' (took {totalMs}ms: read={readMs}, rename={renameMs}, purge={purgeMs}, extents={extMs})");

            FileLogger.Log(LOG_PREFIX,
                $"  Rename: total={r.TotalBtr}, renamed={r.Renamed}, " +
                $"kept=[A1:{r.KeepAsIs_A1}, CAS_HEAD:{r.KeepAsIs_CasHead}], " +
                $"skipped=[xref:{r.Skipped_Xref}, layout:{r.Skipped_Layout}, anon:{r.Skipped_Anonymous}, " +
                $"conflict:{r.Skipped_Conflict}, empty:{r.Skipped_Empty}], failed={r.Failed}");

            FileLogger.Log(LOG_PREFIX,
                $"  Purge: erased={p.TotalErased} across {p.Passes} pass(es)" +
                (p.HitMaxPasses ? " [CẢNH BÁO: đạt max passes, có thể còn dư]" : ""));

            // Entity type breakdown — giúp user thấy source DWG chứa type gì, tách with-extents vs no-extents.
            LogModelSpaceTypeBreakdown(e);

            if (e.HasExtents)
            {
                FileLogger.Log(LOG_PREFIX,
                    $"  ModelSpace: {e.TotalEntities} entities ({e.EntitiesWithExtents} có extents, {e.EntitiesNoExtents} không có), " +
                    $"bbox=[({e.Extents.MinPoint.X:F2},{e.Extents.MinPoint.Y:F2})-({e.Extents.MaxPoint.X:F2},{e.Extents.MaxPoint.Y:F2})], " +
                    $"w={e.Width:F2}, h={e.Height:F2}");

                if (e.Width >= BBOX_OUTLIER_WARN || e.Height >= BBOX_OUTLIER_WARN)
                {
                    FileLogger.Log(LOG_PREFIX,
                        $"  CẢNH BÁO bbox '{fileName}' quá lớn (w={e.Width / 1000:F1}m, h={e.Height / 1000:F1}m) — " +
                        $"có thể có entity 'orphan' trong Model Space kéo bbox. Layout kế sẽ dời xa tương ứng.");

                    // Liệt kê top-3 entity có center xa median center nhất — ứng viên outlier.
                    var outliers = FindTopOutliers(e.EntityInfos, 3);
                    for (int k = 0; k < outliers.Count; k++)
                    {
                        var o = outliers[k];
                        FileLogger.Log(LOG_PREFIX,
                            $"    Outlier #{k + 1}: {o.TypeName} handle=0x{o.HandleValue:X} layer='{o.Layer}' " +
                            $"center=({o.CenterX:F0},{o.CenterY:F0}) size=({o.Width:F0}×{o.Height:F0})");
                    }
                }
            }
            else
            {
                FileLogger.Log(LOG_PREFIX,
                    $"  ModelSpace: {e.TotalEntities} entities, hasBbox=False (rỗng hoặc không entity nào có extents).");
            }

            // Keep-as-is block references trong Model Space nguồn.
            if (e.KeepAsIsRefsInSource.Count > 0)
            {
                var groupSummary = string.Join(", ",
                    e.KeepAsIsRefsInSource.GroupBy(x => x.BlockName)
                        .Select(g => $"{g.Key}×{g.Count()}"));
                FileLogger.Log(LOG_PREFIX, $"  Keep-as-is refs in source ModelSpace: {groupSummary}");
                foreach (var kr in e.KeepAsIsRefsInSource)
                {
                    FileLogger.Log(LOG_PREFIX,
                        $"    [src] {kr.BlockName} handle=0x{kr.HandleValue:X} " +
                        $"pos=({kr.Position.X:F0},{kr.Position.Y:F0}) rot={kr.Rotation * 180 / Math.PI:F1}° " +
                        $"bbox=[({kr.Bbox.MinPoint.X:F0},{kr.Bbox.MinPoint.Y:F0})-({kr.Bbox.MaxPoint.X:F0},{kr.Bbox.MaxPoint.Y:F0})]");
                }
            }
        }

        /// <summary>Đổi tên các BlockTableRecord (trừ A1 / CAS_HEAD / layout / anonymous). Trả breakdown chi tiết.</summary>
        private RenameStats RenameBlocksInSideDb(Database sideDb, string prefix)
        {
            var stats = new RenameStats();
            using (var tr = sideDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);

                // Snapshot trước khi UpgradeOpen — tránh vòng lặp collection bị mutate.
                var ids = new List<ObjectId>();
                foreach (ObjectId id in bt) ids.Add(id);
                stats.TotalBtr = ids.Count;

                // Plan đổi tên.
                var renamePlan = new List<(ObjectId Id, string NewName)>();
                foreach (ObjectId id in ids)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);

                    if (btr.IsLayout) { stats.Skipped_Layout++; continue; }
                    if (btr.IsFromExternalReference) { stats.Skipped_Xref++; continue; }
                    if (btr.IsAnonymous) { stats.Skipped_Anonymous++; continue; }

                    string name = btr.Name;
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("*", StringComparison.Ordinal))
                    {
                        stats.Skipped_Empty++;
                        continue;
                    }

                    if (name.Equals("A1", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.KeepAsIs_A1++;
                        continue;
                    }
                    if (name.Equals("CAS_HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        stats.KeepAsIs_CasHead++;
                        continue;
                    }

                    string candidate = $"{prefix}_{name}";
                    if (candidate.Equals(name, StringComparison.OrdinalIgnoreCase)) { stats.Skipped_Empty++; continue; }
                    if (bt.Has(candidate)) { stats.Skipped_Conflict++; continue; }

                    renamePlan.Add((id, candidate));
                }

                foreach (var entry in renamePlan)
                {
                    try
                    {
                        var btr = (BlockTableRecord)tr.GetObject(entry.Id, OpenMode.ForWrite);
                        btr.Name = entry.NewName;
                        stats.Renamed++;
                    }
                    catch (Exception ex)
                    {
                        stats.Failed++;
                        FileLogger.Log(LOG_PREFIX, $"Rename block FAIL: {ex.Message}");
                    }
                }

                tr.Commit();
            }
            return stats;
        }

        /// <summary>
        /// P3 — Rename anonymous BlockTableRecords được reference bởi BlockReference trong ModelSpace.
        /// Mục đích: tránh WblockCloneObjects silent-drop khi dest template chứa anon blocks trùng tên
        /// (DuplicateRecordCloning.Ignore discard cả symbol lẫn entity reference tới nó).
        ///
        /// Strategy: chỉ rename anon BTR nào được BlockReference trong MS dùng — KHÔNG đụng tới
        /// anon BTR của Dimension (*D), Hatch (*H), Group (*A) vì chúng không xuất hiện trực tiếp
        /// trong Clone flow và rename có thể phá dependency.
        ///
        /// New name format: `{prefix}_Anon_{counter}` — non-anon, unique per file.
        /// </summary>
        private void RenameReferencedAnonBlocksInSideDb(Database sideDb, string prefix, RenameStats stats)
        {
            using (var tr = sideDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(BlockTableRecord.ModelSpace)) { tr.Commit(); return; }

                // Step 1: collect anon BTR ObjectIds referenced từ ModelSpace.
                var refAnonIds = new HashSet<ObjectId>();
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId eid in ms)
                {
                    var br = tr.GetObject(eid, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    // DynamicBlockTableRecord nếu dynamic, BlockTableRecord nếu static.
                    ObjectId btrId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                    if (btrId.IsNull) continue;

                    try
                    {
                        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                        if (btr.IsAnonymous && !btr.IsFromExternalReference)
                            refAnonIds.Add(btrId);
                    }
                    catch { /* BTR invalid — skip */ }
                }

                // Step 2: collect all anon BTRs để phân loại "referenced vs unreferenced".
                foreach (ObjectId id in bt)
                {
                    try
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (!btr.IsAnonymous) continue;
                        if (btr.IsLayout || btr.IsFromExternalReference) continue;

                        if (!refAnonIds.Contains(id))
                        {
                            stats.AnonSkipped_Unreferenced++;
                            continue;
                        }
                    }
                    catch { continue; }
                }

                // Step 3: generate unique names + rename. Counter tăng dần để tránh conflict giữa các anon.
                int counter = 1;
                foreach (ObjectId id in refAnonIds)
                {
                    string candidate;
                    do
                    {
                        candidate = $"{prefix}_Anon_{counter++}";
                    } while (bt.Has(candidate));

                    try
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                        string oldName = btr.Name;
                        bool oldAnon = btr.IsAnonymous;
                        btr.Name = candidate;
                        // D2 — Read IsAnonymous SAU rename để verify flag internal có đổi không.
                        // Nếu IsAnonymous vẫn true sau rename → xác nhận Giả thuyết A: AutoCAD giữ flag
                        // bất kể Name → WblockClone vẫn treat as anon → silent-drop.
                        bool newAnon = btr.IsAnonymous;
                        stats.AnonRenamed++;
                        FileLogger.Log(LOG_PREFIX,
                            $"    Anon renamed: '{oldName}' (IsAnonymous={oldAnon}) → '{candidate}' (IsAnonymous={newAnon})");
                    }
                    catch (Exception ex)
                    {
                        stats.AnonFailed++;
                        FileLogger.Log(LOG_PREFIX, $"    Anon rename FAIL (id={id}): {ex.Message}");
                    }
                }

                tr.Commit();
            }

            if (stats.AnonRenamed > 0 || stats.AnonFailed > 0)
            {
                FileLogger.Log(LOG_PREFIX,
                    $"  Anon rename: {stats.AnonRenamed} renamed, " +
                    $"{stats.AnonSkipped_Unreferenced} skipped (unreferenced — sẽ purge), " +
                    $"{stats.AnonFailed} failed.");
            }
        }

        /// <summary>Purge recursively tới khi không còn gì purge được. Trả stats: erased count + số pass.</summary>
        private PurgeStats PurgeUnusedInSideDb(Database sideDb)
        {
            var stats = new PurgeStats();
            const int maxPasses = 10;
            for (int pass = 0; pass < maxPasses; pass++)
            {
                int erasedThisPass = 0;
                using (var tr = sideDb.TransactionManager.StartTransaction())
                {
                    var candidates = CollectPurgeCandidates(tr, sideDb);
                    if (candidates.Count == 0) { tr.Commit(); break; }

                    // Purge filter in-place — giữ lại chỉ id thực sự purgeable.
                    sideDb.Purge(candidates);

                    foreach (ObjectId id in candidates)
                    {
                        try
                        {
                            var obj = tr.GetObject(id, OpenMode.ForWrite);
                            if (obj != null && !obj.IsErased) { obj.Erase(); erasedThisPass++; }
                        }
                        catch { /* bỏ qua id không erase được */ }
                    }
                    tr.Commit();
                }

                stats.Passes = pass + 1;
                stats.TotalErased += erasedThisPass;
                if (erasedThisPass == 0) break;

                // Cờ "chạm trần": nếu pass cuối cùng vẫn còn erase, có thể còn dư chưa purge hết.
                if (pass == maxPasses - 1 && erasedThisPass > 0) stats.HitMaxPasses = true;
            }
            return stats;
        }

        private ObjectIdCollection CollectPurgeCandidates(Transaction tr, Database db)
        {
            var ids = new ObjectIdCollection();

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt) ids.Add(id);

            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId id in ltt) ids.Add(id);

            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in tst) ids.Add(id);

            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in dst) ids.Add(id);

            // Block — bỏ Model/Paper layout BTR.
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (btr.IsLayout) continue;
                ids.Add(id);
            }

            var vpt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
            foreach (ObjectId id in vpt) ids.Add(id);

            return ids;
        }

        private ExtentsStats ComputeModelSpaceExtents(Database sideDb)
        {
            var stats = new ExtentsStats();
            Extents3d acc = new Extents3d();
            bool init = false;

            using (var tr = sideDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(sideDb.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(BlockTableRecord.ModelSpace)) { tr.Commit(); stats.Extents = acc; return stats; }

                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    stats.TotalEntities++;

                    string typeName = ent.GetType().Name;

                    // Tách riêng try/catch cho GeometricExtents để scan keep-as-is vẫn chạy
                    // kể cả khi extents throw (thường xảy ra với BlockReference trong side db chưa graphics-realized).
                    bool hasExt = false;
                    Extents3d ext = new Extents3d();
                    try
                    {
                        ext = ent.GeometricExtents;
                        hasExt = true;
                        stats.EntitiesWithExtents++;
                        IncType(stats.TypeCountWithExtents, typeName);
                    }
                    catch
                    {
                        stats.EntitiesNoExtents++;
                        IncType(stats.TypeCountNoExtents, typeName);
                    }

                    if (hasExt)
                    {
                        if (!init) { acc = ext; init = true; }
                        else acc.AddExtents(ext);

                        stats.EntityInfos.Add(new EntityExtInfo
                        {
                            TypeName = ent.GetType().Name,
                            HandleHex = ent.Handle.ToString(),
                            HandleValue = ent.Handle.Value,
                            CenterX = (ext.MinPoint.X + ext.MaxPoint.X) / 2,
                            CenterY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2,
                            Width = ext.MaxPoint.X - ext.MinPoint.X,
                            Height = ext.MaxPoint.Y - ext.MinPoint.Y,
                            Layer = ent.Layer
                        });
                    }

                    // Scan keep-as-is: chạy bất kể extents có thành công hay không.
                    if (ent is BlockReference brSrc)
                    {
                        try
                        {
                            string effName = GetEffectiveBlockName(brSrc, tr);
                            if (effName != null && KeepAsIsBlocks.Contains(effName))
                            {
                                stats.KeepAsIsRefsInSource.Add(new KeepAsIsRefInfo
                                {
                                    BlockName = effName,
                                    Position = brSrc.Position,
                                    Rotation = brSrc.Rotation,
                                    Bbox = hasExt ? ext : new Extents3d(brSrc.Position, brSrc.Position),
                                    HandleValue = brSrc.Handle.Value
                                });
                            }
                        }
                        catch { /* name resolve fail — bỏ qua */ }
                    }
                }
                tr.Commit();
            }

            stats.HasExtents = init;
            stats.Extents = acc;
            if (init)
            {
                stats.Width = acc.MaxPoint.X - acc.MinPoint.X;
                stats.Height = acc.MaxPoint.Y - acc.MinPoint.Y;
            }
            return stats;
        }

        /// <summary>Tăng counter cho type name trong dictionary.</summary>
        private static void IncType(Dictionary<string, int> dict, string key)
        {
            if (dict.TryGetValue(key, out int v)) dict[key] = v + 1;
            else dict[key] = 1;
        }

        /// <summary>
        /// Log breakdown entity types trong ModelSpace source — tách [HAS EXTENTS] / [NO EXTENTS].
        /// Giúp user thấy DWG có type gì, diagnose silent-drop khi bbox nhỏ hơn expected.
        /// Cảnh báo nếu có nhiều entity no-extents (có thể là anon BlockReference bị AutoCAD suppress graphics).
        /// </summary>
        private static void LogModelSpaceTypeBreakdown(ExtentsStats e)
        {
            if (e.TotalEntities == 0) return;

            if (e.TypeCountWithExtents.Count > 0)
            {
                FileLogger.Log(LOG_PREFIX, "  ModelSpace type breakdown [HAS EXTENTS]:");
                foreach (var kv in e.TypeCountWithExtents.OrderByDescending(x => x.Value))
                    FileLogger.Log(LOG_PREFIX, $"    • {kv.Key}: {kv.Value}");
            }

            if (e.TypeCountNoExtents.Count > 0)
            {
                FileLogger.Log(LOG_PREFIX,
                    "  ModelSpace type breakdown [NO EXTENTS] (entity không render bbox — thường do block def rỗng / hidden):");
                foreach (var kv in e.TypeCountNoExtents.OrderByDescending(x => x.Value))
                    FileLogger.Log(LOG_PREFIX, $"    • {kv.Key}: {kv.Value}");

                // Nếu hầu hết entity không có extents → cảnh báo rủi ro silent-drop ở Phase 2.
                if (e.EntitiesNoExtents >= e.TotalEntities / 2)
                {
                    FileLogger.Log(LOG_PREFIX,
                        $"    CẢNH BÁO: {e.EntitiesNoExtents}/{e.TotalEntities} entity không có extents — " +
                        $"có thể content ẩn trong anonymous block definitions. Clone có thể silent-drop nếu dest trùng tên.");
                }
            }
        }

        #endregion
    }
}
