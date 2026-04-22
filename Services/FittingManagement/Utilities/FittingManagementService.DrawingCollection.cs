using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Drawing Collection — gom Model Space của nhiều .dwg vào bản vẽ hiện hành.
    /// Quy trình mỗi file:
    ///   1. Đọc vào side database (không đụng file gốc trên đĩa).
    ///   2. Đổi tên mọi block [TênFile]_[TênBlock] — giữ nguyên A1 và CAS_HEAD.
    ///   3. Purge recursively các định nghĩa không dùng (layer, block, linetype...).
    ///   4. Tính bbox Model Space → width.
    ///   5. WblockCloneObjects modelspace entity sang current space của dest.
    ///   6. TransformBy offsetX ngang; offsetX += width + 1000 cho file kế.
    /// DuplicateRecordCloning.Ignore → khi trùng tên (A1, CAS_HEAD...) giữ định nghĩa bản vẽ gốc.
    /// Sau khi xong: ZOOM EXTENTS qua SendStringToExecute.
    /// Log chi tiết đủ để đánh giá chất lượng kết quả qua text thuần (không cần screenshot UI).
    /// </summary>
    public partial class FittingManagementService
    {
        private const double COLLECTION_GAP = 1000.0;

        // Ngưỡng cảnh báo bbox outlier: 100m = 100,000mm. Bản vẽ cơ khí thông thường
        // hiếm khi vượt ngưỡng này; vượt → nghi ngờ entity "orphan" ngoài modelspace chính.
        private const double BBOX_OUTLIER_WARN = 100000.0;

        #region Public entry

        public async Task<ImportResult> CollectDrawingsAsync(string[] dwgPaths, IProgress<string> progress = null)
        {
            FileLogger.LogSessionStart($"CollectDrawings ({dwgPaths?.Length ?? 0} files)");
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu CollectDrawingsAsync — {dwgPaths?.Length ?? 0} files.");

            var result = new ImportResult();
            var totalSw = Stopwatch.StartNew();

            if (dwgPaths == null || dwgPaths.Length == 0)
            {
                Debug.WriteLine($"{LOG_PREFIX} Không có file nào được chọn.");
                return result;
            }

            // Xác định path của dest doc (nhà kho hiện đang mở) để detect self-collect.
            // Nếu user lỡ chọn chính file nhà kho → cần skip + warn, tránh nhân đôi/phá bản vẽ hiện tại.
            string destDocPath = null;
            try
            {
                var activeDoc = Application.DocumentManager.MdiActiveDocument;
                if (activeDoc != null)
                {
                    string dbFilename = activeDoc.Database.Filename;
                    if (!string.IsNullOrWhiteSpace(dbFilename))
                        destDocPath = Path.GetFullPath(dbFilename);
                }
            }
            catch { /* doc chưa save hoặc resolve path fail — bỏ qua check, coi như không có dest path */ }

            if (destDocPath != null)
                FileLogger.Log(LOG_PREFIX, $"Dest doc (nhà kho) path: {destDocPath}");

            // PHASE 1 — preprocess side db (đọc / rename / purge / measure extents) trên worker thread.
            var phase1Sw = Stopwatch.StartNew();
            List<PreparedDrawing> prepared = await Task.Run(() => PreprocessAll(dwgPaths, progress, result, destDocPath));
            phase1Sw.Stop();
            FileLogger.Log(LOG_PREFIX,
                $"--- PHASE 1 (preprocess) xong sau {phase1Sw.ElapsedMilliseconds}ms — {prepared.Count}/{dwgPaths.Length} file(s) preparedOK.");

            // PHASE 2 — clone vào current doc (PHẢI UI thread).
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                foreach (var p in prepared) p.SideDb?.Dispose();
                throw new InvalidOperationException("Không có document đang hoạt động.");
            }

            var phase2Sw = Stopwatch.StartNew();
            double firstOffsetX = 0, lastOffsetX = 0;
            using (DocumentLock docLock = doc.LockDocument())
            {
                // Scan dest ModelSpace tìm A1/CAS_HEAD existing → collect mới nối tiếp sau.
                double offsetX = ComputeInitialOffsetX(doc.Database);
                firstOffsetX = offsetX;

                for (int i = 0; i < prepared.Count; i++)
                {
                    var item = prepared[i];
                    progress?.Report($"[{i + 1}/{prepared.Count}] Đang chèn: {item.FileName}");

                    var fileSw = Stopwatch.StartNew();
                    try
                    {
                        var cloneStats = CloneToCurrentSpace(doc.Database, item, offsetX);
                        item.CloneStats = cloneStats;
                        fileSw.Stop();

                        // Label rõ nghĩa: `topLevel` = entity Model Space cấp cao nhất (= srcIds);
                        // `totalPrimaries` = IdPair.IsPrimary count (bao gồm attribute/vertex/nested sub-entity được kéo theo).
                        // Transform chỉ áp lên top-level (không gọi lên sub-entity — chúng đi theo parent).
                        FileLogger.Log(LOG_PREFIX,
                            $"  Clone '{item.FileName}': topLevel={cloneStats.SrcIds} → " +
                            $"totalPrimaries (incl. nested sub-entities)={cloneStats.PrimaryCloned}, " +
                            $"symbols [cloned={cloneStats.SymbolsCloned}, ignored={cloneStats.SymbolsIgnored}], " +
                            $"transformed={cloneStats.Transformed} (failed={cloneStats.TransformFailed}), " +
                            $"dx={cloneStats.Dx:F2}, took={fileSw.ElapsedMilliseconds}ms");

                        // Log vị trí/bbox mỗi A1/CAS_HEAD đã clone + transform vào dest.
                        // Đây là nguồn dữ liệu cho overlap detection ở summary.
                        foreach (var ki in cloneStats.KeepAsIsCloned)
                        {
                            FileLogger.Log(LOG_PREFIX,
                                $"    [dest] {ki.BlockName} handle=0x{ki.DestHandleValue:X} " +
                                $"pos=({ki.Position.X:F0},{ki.Position.Y:F0}) rot={ki.Rotation * 180 / Math.PI:F1}° " +
                                $"bbox=[({ki.Bbox.MinPoint.X:F0},{ki.Bbox.MinPoint.Y:F0})-({ki.Bbox.MaxPoint.X:F0},{ki.Bbox.MaxPoint.Y:F0})]");
                        }

                        // A1-aware layout: effective width = max(content width, A1 extent tới cạnh phải).
                        // Lý do: A1 (khung tên) có thể extend ra ngoài content bbox (vd content=15407, A1=17146
                        // → A1 thừa 1739mm sẽ đè lên file kế nếu chỉ dùng content_width + 1000 gap).
                        // Chọn MAX để: (a) file content lớn hơn A1 (như file 2742m) giữ nguyên, (b) file A1 rộng hơn
                        // content thì dời offsetX đủ xa để A1 không đè file kế.
                        double contentW = item.HasExtents ? item.Width : 0;
                        double effectiveW = contentW;
                        foreach (var ki in cloneStats.KeepAsIsCloned)
                        {
                            double a1MaxXRel = ki.Bbox.MaxPoint.X - offsetX;
                            if (a1MaxXRel > effectiveW) effectiveW = a1MaxXRel;
                        }

                        if (effectiveW > contentW && cloneStats.KeepAsIsCloned.Count > 0)
                        {
                            FileLogger.Log(LOG_PREFIX,
                                $"    Layout width adjusted: content={contentW:F0} → effective={effectiveW:F0} " +
                                $"(A1 extend {effectiveW - contentW:F0}mm beyond content — dùng effective để tránh overlap)");
                        }

                        double prevOffset = offsetX;
                        bool advance = item.HasExtents || cloneStats.KeepAsIsCloned.Count > 0;
                        if (advance)
                            offsetX += effectiveW + COLLECTION_GAP;

                        item.EffectiveWidth = effectiveW;
                        item.Advanced = advance;

                        result.SuccessCount++;
                        item.PlacedOffsetX = prevOffset;
                        lastOffsetX = offsetX;
                        FileLogger.Log(LOG_PREFIX,
                            $"  Collected OK: {item.FileName} placed at x={prevOffset:F2} → next offsetX={offsetX:F2}");
                    }
                    catch (Exception ex)
                    {
                        fileSw.Stop();
                        result.FailCount++;
                        result.AddError(item.FileName, ex.Message);
                        FileLogger.LogException(LOG_PREFIX, $"Clone {item.FileName}", ex);
                    }
                    finally
                    {
                        item.SideDb?.Dispose();
                    }
                }

                // Zoom Extents sau khi xếp xong. SendStringToExecute deferred → chạy sau khi DocumentLock release.
                try
                {
                    doc.SendStringToExecute("_.ZOOM _E ", true, false, true);
                    FileLogger.Log(LOG_PREFIX, "Đã queue lệnh ZOOM _E (deferred, chạy sau khi unlock doc).");
                }
                catch (Exception ex)
                {
                    FileLogger.LogException(LOG_PREFIX, "Zoom Extents", ex);
                }
            }
            phase2Sw.Stop();
            FileLogger.Log(LOG_PREFIX,
                $"--- PHASE 2 (clone) xong sau {phase2Sw.ElapsedMilliseconds}ms — " +
                $"success={result.SuccessCount}, failed={result.FailCount}.");

            totalSw.Stop();
            WriteFinalSummary(dwgPaths, prepared, result, firstOffsetX, lastOffsetX,
                phase1Sw.ElapsedMilliseconds, phase2Sw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds);

            progress?.Report($"Hoàn tất — Thành công: {result.SuccessCount}, Thất bại: {result.FailCount}");
            Debug.WriteLine($"{LOG_PREFIX} CollectDrawingsAsync HOÀN TẤT — {result.SuccessCount}/{dwgPaths.Length}.");
            return result;
        }

        #endregion

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
                // Detect bằng so sánh full path case-insensitive (Windows filesystem convention).
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
                    // File DWG cần chạy RECOVER trong AutoCAD trước. Không che giấu — thông báo rõ cho user.
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
                    // User có thể dùng handle (hex) với lệnh `_.SELECT` hoặc `(handent "H")` trong AutoCAD để chọn.
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

            // Keep-as-is block references được phát hiện trong Model Space nguồn.
            // Đây là các BlockReference đến block A1/CAS_HEAD nằm TRỰC TIẾP trong Model Space (không phải Paper Space/Layout).
            // Tại phase 2, mỗi reference này sẽ được clone và có thể tạo overlap trong dest nếu nhiều file cùng có A1.
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

                // Plan đổi tên (tránh đụng hàng khi rename trực tiếp trong lúc iterate BlockTable).
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

            // Layer
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt) ids.Add(id);

            // Linetype
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (ObjectId id in ltt) ids.Add(id);

            // Text style
            var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in tst) ids.Add(id);

            // Dim style
            var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in dst) ids.Add(id);

            // Block — bỏ Model/Paper layout BTR
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (btr.IsLayout) continue;
                ids.Add(id);
            }

            // View / UCS / Viewport / RegApp style table
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

                    // Tách riêng try/catch cho GeometricExtents để việc scan keep-as-is vẫn chạy
                    // kể cả khi extents throw (thường xảy ra với BlockReference trong side db chưa
                    // graphics-realized — nhưng Name/Position/DynamicBlockTableRecord vẫn đọc được).
                    bool hasExt = false;
                    Extents3d ext = new Extents3d();
                    try
                    {
                        ext = ent.GeometricExtents;
                        hasExt = true;
                        stats.EntitiesWithExtents++;
                    }
                    catch
                    {
                        stats.EntitiesNoExtents++;
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
                    // Nếu extents throw, vẫn lưu Position; Bbox dùng fallback (1 điểm) để không gây NRE ở downstream.
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

        /// <summary>
        /// Tìm top-N entity có center xa median center nhất — candidate outlier kéo bbox.
        /// Dùng median thay vì mean để không bị chính outlier làm lệch tâm.
        /// </summary>
        private static List<EntityExtInfo> FindTopOutliers(List<EntityExtInfo> entities, int count)
        {
            if (entities == null || entities.Count == 0) return new List<EntityExtInfo>();

            var xs = entities.Select(e => e.CenterX).ToList();
            var ys = entities.Select(e => e.CenterY).ToList();
            double medX = Median(xs);
            double medY = Median(ys);

            return entities
                .OrderByDescending(e =>
                {
                    double dx = e.CenterX - medX;
                    double dy = e.CenterY - medY;
                    return dx * dx + dy * dy; // không cần Sqrt, rank không đổi
                })
                .Take(count)
                .ToList();
        }

        private static double Median(List<double> vals)
        {
            if (vals == null || vals.Count == 0) return 0;
            var sorted = vals.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return (sorted.Count % 2 == 0) ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }

        #endregion

        #region Phase 2 — clone vào current doc (UI thread)

        /// <summary>
        /// Clone toàn bộ modelspace của sideDb vào current space của dest db,
        /// sau đó dịch các entity cloned theo offsetX (chiều ngang) sao cho:
        ///   - min X của bbox nằm đúng tại vị trí offsetX hiện tại.
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
                }
                else
                {
                    if (pair.IsCloned) stats.SymbolsCloned++;
                    else stats.SymbolsIgnored++;
                }
            }

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

        #region End-of-run summary

        /// <summary>
        /// Log 1 block tổng kết ở cuối run — đủ thông tin để đánh giá quality mà không cần screenshot UI.
        /// Bao gồm: per-file compact table, totals, layout sanity check (sum widths + gaps vs span).
        /// </summary>
        private void WriteFinalSummary(
            string[] requestedPaths, List<PreparedDrawing> prepared, ImportResult result,
            double firstOffsetX, double lastOffsetX,
            long phase1Ms, long phase2Ms, long totalMs)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine("====== DRAWING COLLECTION — SUMMARY ======");
            sb.AppendLine($"Requested: {requestedPaths.Length} file(s) | Success: {result.SuccessCount} | Failed: {result.FailCount}");

            // Per-file compact table — chỉ file preparedOK (lỗi đã log riêng phía trên).
            if (prepared.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Per-file breakdown:");
                sb.AppendLine("  #  | Width(mm) | Height(mm) | Entities | Renamed | Purged | Placed@X   | File");

                int totalEntitiesMs = 0, totalRenamed = 0, totalPurged = 0;
                int totalKeptA1 = 0, totalKeptCasHead = 0;
                int totalPrimaryCloned = 0, totalSymbolsIgnored = 0, totalTransformFailed = 0;
                double sumWidths = 0;

                for (int i = 0; i < prepared.Count; i++)
                {
                    var p = prepared[i];
                    string wStr = p.HasExtents ? p.Width.ToString("F0") : "N/A";
                    string hStr = p.HasExtents ? (p.Extents.MaxPoint.Y - p.Extents.MinPoint.Y).ToString("F0") : "N/A";
                    int ents = p.ExtStats?.TotalEntities ?? 0;
                    int rnm = p.RenameStats?.Renamed ?? 0;
                    int prg = p.PurgeStats?.TotalErased ?? 0;
                    string placedStr = p.PlacedOffsetX.HasValue ? p.PlacedOffsetX.Value.ToString("F0") : "--";

                    sb.AppendLine($"  {i + 1,2} | {wStr,9} | {hStr,10} | {ents,8} | {rnm,7} | {prg,6} | {placedStr,10} | {p.FileName}");

                    if (p.HasExtents) sumWidths += p.Width;
                    totalEntitiesMs += ents;
                    totalRenamed += rnm;
                    totalPurged += prg;
                    totalKeptA1 += p.RenameStats?.KeepAsIs_A1 ?? 0;
                    totalKeptCasHead += p.RenameStats?.KeepAsIs_CasHead ?? 0;
                    if (p.CloneStats != null)
                    {
                        totalPrimaryCloned += p.CloneStats.PrimaryCloned;
                        totalSymbolsIgnored += p.CloneStats.SymbolsIgnored;
                        totalTransformFailed += p.CloneStats.TransformFailed;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Totals:");
                sb.AppendLine($"  ModelSpace entities scanned   : {totalEntitiesMs}");
                sb.AppendLine($"  Entities cloned into dest doc : {totalPrimaryCloned}");
                sb.AppendLine($"  Symbols ignored (dup-name)    : {totalSymbolsIgnored}" +
                              "   (dest giữ định nghĩa, src bỏ — đúng thiết kế khi trùng layer/block/linetype)");
                sb.AppendLine($"  TransformBy failures          : {totalTransformFailed}" +
                              (totalTransformFailed > 0 ? "   ⚠ nên kiểm log chi tiết phía trên" : ""));
                sb.AppendLine($"  Blocks renamed                : {totalRenamed}");
                sb.AppendLine($"  Blocks kept as-is [A1]        : {totalKeptA1}");
                sb.AppendLine($"  Blocks kept as-is [CAS_HEAD]  : {totalKeptCasHead}");
                sb.AppendLine($"  Purge: erased total           : {totalPurged}");

                // Sanity-check layout: lastOffsetX ≈ firstOffsetX + sum(effectiveWidth) + (N_advanced) × GAP.
                // firstOffsetX != 0 nếu dest đã có A1/CAS_HEAD → collect nối tiếp từ Xmax+GAP.
                int advancedFiles = prepared.Count(p => p.Advanced);
                double sumEffective = prepared.Where(p => p.Advanced).Sum(p => p.EffectiveWidth);
                double expectedSpan = firstOffsetX + sumEffective + advancedFiles * COLLECTION_GAP;
                int filesAdjustedByA1 = prepared.Count(p => p.Advanced && p.EffectiveWidth > p.Width + 0.5);
                sb.AppendLine();
                sb.AppendLine("Layout sanity:");
                sb.AppendLine($"  Initial offsetX      : {firstOffsetX:F0}mm " +
                              (firstOffsetX > 0 ? "(nối tiếp sau A1 existing trong dest)" : "(dest trống A1)"));
                sb.AppendLine($"  Sum content widths   : {sumWidths:F0}mm");
                sb.AppendLine($"  Sum effective widths : {sumEffective:F0}mm " +
                              (filesAdjustedByA1 > 0 ? $"({filesAdjustedByA1} file mở rộng vì A1 > content)" : "(không file nào cần mở rộng)"));
                sb.AppendLine($"  Files advanced        : {advancedFiles} → gaps = {advancedFiles} × {COLLECTION_GAP:F0} = {advancedFiles * COLLECTION_GAP:F0}mm");
                sb.AppendLine($"  Expected next offsetX after last file : {expectedSpan:F0}mm");
                sb.AppendLine($"  Actual   next offsetX after last file : {lastOffsetX:F0}mm");
                double delta = Math.Abs(expectedSpan - lastOffsetX);
                if (delta > 0.5)
                    sb.AppendLine($"  ⚠ Delta {delta:F2}mm — không khớp, có thể 1 file fail ở phase 2.");
                else
                    sb.AppendLine($"  ✓ Khớp (delta={delta:F2}mm).");

                // Keep-as-is overlap detection — scan mọi A1/CAS_HEAD đã clone vào dest,
                // tìm các cặp có bbox chồng lên nhau. Đây là chẩn đoán cho vấn đề "A1 chèn lên nhau".
                var allKeepAsIs = new List<(string FileName, KeepAsIsClonedInfo Info)>();
                foreach (var p in prepared)
                {
                    if (p.CloneStats?.KeepAsIsCloned == null) continue;
                    foreach (var ki in p.CloneStats.KeepAsIsCloned)
                        allKeepAsIs.Add((p.FileName, ki));
                }

                sb.AppendLine();
                sb.AppendLine($"Keep-as-is blocks in dest (A1/CAS_HEAD): {allKeepAsIs.Count} reference(s)");
                if (allKeepAsIs.Count > 0)
                {
                    for (int i = 0; i < allKeepAsIs.Count; i++)
                    {
                        var (fn, info) = allKeepAsIs[i];
                        sb.AppendLine($"  [{i + 1}] {info.BlockName} từ '{fn}' handle=0x{info.DestHandleValue:X} " +
                                      $"pos=({info.Position.X:F0},{info.Position.Y:F0}) " +
                                      $"bbox=[({info.Bbox.MinPoint.X:F0},{info.Bbox.MinPoint.Y:F0})-({info.Bbox.MaxPoint.X:F0},{info.Bbox.MaxPoint.Y:F0})]");
                    }

                    var overlaps = FindKeepAsIsOverlaps(allKeepAsIs);
                    if (overlaps.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"⚠ OVERLAP DETECTED — {overlaps.Count} cặp Keep-as-is block chèn lên nhau:");
                        foreach (var ov in overlaps)
                        {
                            var (fn1, k1) = allKeepAsIs[ov.IndexA];
                            var (fn2, k2) = allKeepAsIs[ov.IndexB];
                            sb.AppendLine($"  [{ov.IndexA + 1}] ↔ [{ov.IndexB + 1}]: " +
                                          $"{k1.BlockName} '{fn1}' (0x{k1.DestHandleValue:X}) " +
                                          $"vs {k2.BlockName} '{fn2}' (0x{k2.DestHandleValue:X})");
                            sb.AppendLine($"      Overlap area={ov.OverlapArea:F0}mm² " +
                                          $"(~{ov.PctOfSmaller:F1}% của block nhỏ hơn), " +
                                          $"offset pos=({k2.Position.X - k1.Position.X:F0},{k2.Position.Y - k1.Position.Y:F0})mm");
                        }
                        sb.AppendLine("  → Lý do thường gặp: (1) A1/CAS_HEAD được insert TRỰC TIẾP trong Model Space của nhiều file (không phải Paper Space); " +
                                      "(2) file width nhỏ không đủ để 2 khung tên cách nhau; (3) A1 của file trước mở rộng quá bbox chính do có entity đi kèm.");
                    }
                    else
                    {
                        sb.AppendLine("  ✓ Không có overlap giữa các Keep-as-is blocks.");
                    }
                }

                // Duplicate detection — bbox giống + entity count chênh <5% → ứng viên file trùng.
                var dupPairs = FindDuplicateCandidates(prepared);
                if (dupPairs.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ Duplicate candidates (bbox khớp trong tolerance 1mm, entity count chênh <5%):");
                    foreach (var pair in dupPairs)
                    {
                        var p1 = prepared[pair.Item1]; var p2 = prepared[pair.Item2];
                        int e1 = p1.ExtStats?.TotalEntities ?? 0;
                        int e2 = p2.ExtStats?.TotalEntities ?? 0;
                        sb.AppendLine($"  #{pair.Item1 + 1} '{p1.FileName}' (entities={e1}) ≈ #{pair.Item2 + 1} '{p2.FileName}' (entities={e2})");
                    }
                    sb.AppendLine("  → User nên kiểm xem có phải cùng 1 bản vẽ bị copy/save hai lần không.");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Timing: phase1={phase1Ms}ms, phase2={phase2Ms}ms, total={totalMs}ms");
            if (prepared.Count > 0)
                sb.AppendLine($"  Avg/file: phase1={phase1Ms / prepared.Count}ms, phase2={phase2Ms / prepared.Count}ms");
            sb.AppendLine("==========================================");

            FileLogger.Log(LOG_PREFIX, sb.ToString());
        }

        /// <summary>
        /// Tìm các cặp file có bbox gần trùng (tolerance 1mm trên w và h) và entity count chênh &lt;5%.
        /// O(N²) — OK vì user chọn thủ công, N thường ≤ 20.
        /// </summary>
        private static List<Tuple<int, int>> FindDuplicateCandidates(List<PreparedDrawing> prepared)
        {
            const double BBOX_TOLERANCE_MM = 1.0;
            const double ENTITY_COUNT_TOLERANCE_PCT = 0.05;

            var pairs = new List<Tuple<int, int>>();
            for (int i = 0; i < prepared.Count; i++)
            {
                var p1 = prepared[i];
                if (!p1.HasExtents) continue;
                double h1 = p1.Extents.MaxPoint.Y - p1.Extents.MinPoint.Y;
                int e1 = p1.ExtStats?.TotalEntities ?? 0;

                for (int j = i + 1; j < prepared.Count; j++)
                {
                    var p2 = prepared[j];
                    if (!p2.HasExtents) continue;
                    double h2 = p2.Extents.MaxPoint.Y - p2.Extents.MinPoint.Y;
                    int e2 = p2.ExtStats?.TotalEntities ?? 0;

                    if (Math.Abs(p1.Width - p2.Width) > BBOX_TOLERANCE_MM) continue;
                    if (Math.Abs(h1 - h2) > BBOX_TOLERANCE_MM) continue;

                    int maxE = Math.Max(e1, e2);
                    if (maxE == 0) continue;
                    double diffPct = Math.Abs(e1 - e2) / (double)maxE;
                    if (diffPct > ENTITY_COUNT_TOLERANCE_PCT) continue;

                    pairs.Add(Tuple.Create(i, j));
                }
            }
            return pairs;
        }

        #endregion

        #region Helpers & stats types

        /// <summary>Các block khung tên cần giữ nguyên, không prefix tên file.</summary>
        private static readonly HashSet<string> KeepAsIsBlocks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A1", "CAS_HEAD" };

        private static bool IsKeepAsIs(string name) => KeepAsIsBlocks.Contains(name);

        /// <summary>
        /// Error status báo hiệu DWG bị lỗi nội bộ cần chạy AutoCAD RECOVER (eDwgNeedsRecovery, eFileSharingViolation...).
        /// User nên mở file thủ công chạy RECOVER rồi thử lại — không auto-recover trong plugin để tránh che giấu vấn đề.
        /// </summary>
        private static bool IsRecoverableCorruptError(Autodesk.AutoCAD.Runtime.Exception ex)
        {
            var s = ex.ErrorStatus;
            return s == Autodesk.AutoCAD.Runtime.ErrorStatus.DwgNeedsRecovery
                || s == Autodesk.AutoCAD.Runtime.ErrorStatus.DwkLockFileFound
                || s == Autodesk.AutoCAD.Runtime.ErrorStatus.FileAccessErr
                || s == Autodesk.AutoCAD.Runtime.ErrorStatus.FileSharingViolation;
        }

        /// <summary>
        /// Lấy "effective" block name cho BlockReference — nếu là dynamic block thì trả tên của AnonymousBlock gốc.
        /// Dynamic block reference có Name='*U123' (anonymous) nhưng DynamicBlockTableRecord trỏ về BTR gốc.
        /// </summary>
        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                if (br.IsDynamicBlock && !br.DynamicBlockTableRecord.IsNull)
                {
                    var btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (btr != null) return btr.Name;
                }
                return br.Name;
            }
            catch { return br.Name; }
        }

        /// <summary>
        /// Tìm cặp Keep-as-is reference trong dest có bbox 2D chồng lên nhau.
        /// O(N²) — OK vì số A1/CAS_HEAD thường &lt; 50.
        /// </summary>
        private static List<KeepAsIsOverlapPair> FindKeepAsIsOverlaps(
            List<(string FileName, KeepAsIsClonedInfo Info)> all)
        {
            var result = new List<KeepAsIsOverlapPair>();
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    var a = all[i].Info.Bbox;
                    var b = all[j].Info.Bbox;

                    double oxMin = Math.Max(a.MinPoint.X, b.MinPoint.X);
                    double oxMax = Math.Min(a.MaxPoint.X, b.MaxPoint.X);
                    double oyMin = Math.Max(a.MinPoint.Y, b.MinPoint.Y);
                    double oyMax = Math.Min(a.MaxPoint.Y, b.MaxPoint.Y);

                    if (oxMax <= oxMin || oyMax <= oyMin) continue; // không overlap

                    double overlapArea = (oxMax - oxMin) * (oyMax - oyMin);
                    double areaA = (a.MaxPoint.X - a.MinPoint.X) * (a.MaxPoint.Y - a.MinPoint.Y);
                    double areaB = (b.MaxPoint.X - b.MinPoint.X) * (b.MaxPoint.Y - b.MinPoint.Y);
                    double smaller = Math.Min(areaA, areaB);
                    double pct = smaller > 0 ? (overlapArea / smaller * 100) : 0;

                    result.Add(new KeepAsIsOverlapPair
                    {
                        IndexA = i,
                        IndexB = j,
                        OverlapArea = overlapArea,
                        PctOfSmaller = pct
                    });
                }
            }
            return result;
        }

        /// <summary>Thay các ký tự AutoCAD cấm trong block name bằng '_'.</summary>
        private static string SanitizeBlockNamePart(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Drawing";
            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`', '\'' };
            var chars = raw.Select(c => (invalid.Contains(c) || char.IsWhiteSpace(c)) ? '_' : c).ToArray();
            string clean = new string(chars).Trim('_');
            return string.IsNullOrEmpty(clean) ? "Drawing" : clean;
        }

        private class PreparedDrawing
        {
            public string FileName;
            public Database SideDb;
            public bool HasExtents;
            public double Width;
            public Extents3d Extents;

            // Stats gắn vào file để tổng kết ở summary cuối run.
            public RenameStats RenameStats;
            public PurgeStats PurgeStats;
            public ExtentsStats ExtStats;
            public CloneStats CloneStats;
            public double? PlacedOffsetX; // null nếu file fail ở phase 2

            // Layout sau khi apply A1-aware gap:
            //   EffectiveWidth = max(content_width, A1_rightmost_relative_to_offsetX).
            //   Advanced       = true nếu offsetX đã được dời (file rỗng + không A1 → false).
            public double EffectiveWidth;
            public bool Advanced;
        }

        private class RenameStats
        {
            public int TotalBtr;
            public int Renamed;
            public int KeepAsIs_A1;
            public int KeepAsIs_CasHead;
            public int Skipped_Layout;
            public int Skipped_Xref;
            public int Skipped_Anonymous;
            public int Skipped_Conflict; // candidate name đã tồn tại
            public int Skipped_Empty;    // name rỗng hoặc '*' (system)
            public int Failed;
        }

        private class PurgeStats
        {
            public int TotalErased;
            public int Passes;
            public bool HitMaxPasses; // đạt ngưỡng 10 pass mà vẫn còn erase
        }

        private class ExtentsStats
        {
            public bool HasExtents;
            public double Width;
            public double Height;
            public Extents3d Extents;
            public int TotalEntities;
            public int EntitiesWithExtents;
            public int EntitiesNoExtents;

            // Per-entity info — chỉ dùng cho diagnose outlier khi bbox vượt ngưỡng.
            // Không expose ObjectId vì side db bị dispose sau phase 1.
            public List<EntityExtInfo> EntityInfos = new List<EntityExtInfo>();

            // BlockReference có effective name ∈ KeepAsIsBlocks nằm TRỰC TIẾP trong Model Space nguồn.
            // (Không scan Paper Space/Layout — vì phase 2 không clone chúng.)
            public List<KeepAsIsRefInfo> KeepAsIsRefsInSource = new List<KeepAsIsRefInfo>();
        }

        private class EntityExtInfo
        {
            public string TypeName;
            public string HandleHex;    // "7F" giữ dạng Handle.ToString()
            public long HandleValue;    // để format hex chuẩn qua X formatter
            public double CenterX, CenterY;
            public double Width, Height;
            public string Layer;
        }

        /// <summary>Thông tin BlockReference keep-as-is trong SOURCE Model Space.</summary>
        private class KeepAsIsRefInfo
        {
            public string BlockName;
            public Point3d Position;
            public double Rotation;
            public Extents3d Bbox;
            public long HandleValue;
        }

        /// <summary>Thông tin BlockReference keep-as-is đã clone vào DEST (sau TransformBy).</summary>
        private class KeepAsIsClonedInfo
        {
            public string BlockName;
            public Point3d Position;
            public double Rotation;
            public Extents3d Bbox;
            public long DestHandleValue;
        }

        /// <summary>1 cặp overlap giữa 2 Keep-as-is reference trong dest.</summary>
        private class KeepAsIsOverlapPair
        {
            public int IndexA;
            public int IndexB;
            public double OverlapArea;
            public double PctOfSmaller; // overlap area / area của block nhỏ hơn × 100
        }

        private class CloneStats
        {
            public int SrcIds;           // số entity modelspace source
            public int PrimaryCloned;    // IsPrimary && IsCloned — entity thực sự copy
            public int SymbolsCloned;    // !IsPrimary && IsCloned — symbol table record (layer/block/linetype) mới
            public int SymbolsIgnored;   // !IsPrimary && !IsCloned — trùng tên, dùng bản dest
            public int Transformed;      // TransformBy thành công
            public int TransformFailed;
            public double Dx;            // vector dịch X đã áp dụng

            // Keep-as-is BlockReference (A1/CAS_HEAD) đã clone + transform thành công vào dest.
            // Dùng cho overlap detection ở summary cuối run.
            public List<KeepAsIsClonedInfo> KeepAsIsCloned = new List<KeepAsIsClonedInfo>();
        }

        #endregion
    }
}
