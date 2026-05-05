using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
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
    ///   6. TransformBy offsetX ngang; offsetX += width + GAP cho file kế.
    /// DuplicateRecordCloning.Ignore → khi trùng tên (A1, CAS_HEAD...) giữ định nghĩa bản vẽ gốc.
    /// Sau khi xong: ZOOM EXTENTS qua SendStringToExecute.
    ///
    /// File này chứa entry point + constants + memory logging.
    /// Phần còn lại tách sang các partial file trong folder DrawingCollection/:
    ///   - .Preprocess.cs : Phase 1 (read/rename/purge/extents)
    ///   - .Clone.cs      : Phase 2 (WblockCloneObjects + transform)
    ///   - .Summary.cs    : Final summary + overlap/duplicate detection
    ///   - .Helpers.cs    : Static utilities (sanitize name, outlier detection, …)
    ///   - .Types.cs      : Nested stats classes (RenameStats, ExtentsStats, …)
    /// </summary>
    public partial class FittingManagementService
    {
        // Hướng A — A1-frame anchored layout. GAP=100mm cho dim/leader nhỏ thò ra ngoài A1 không
        // chồng A1 file kế. Overflow > GAP sẽ được warn ở Phase 1 + overlap detector ở Summary.
        private const double COLLECTION_GAP = 100.0;

        // Ngưỡng cảnh báo bbox outlier. Bản vẽ cơ khí thông thường hiếm khi vượt ngưỡng này;
        // vượt → nghi ngờ entity "orphan" ngoài modelspace chính.
        private const double BBOX_OUTLIER_WARN = 10.0;

        // Batch lớn → memory peak cao (mỗi sideDb giữ trong RAM đến khi Phase 2 dispose).
        // Ước tính: 1 file mechanical ≈ 100-500MB memory sau load + rename + purge.
        private const int BATCH_SIZE_WARN = 15;

        // Ngưỡng memory cảnh báo trong quá trình chạy (MB). Vượt → log warning, user có thể cân nhắc split batch.
        private const long MEMORY_WARN_MB = 2048;

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

            // Baseline memory + batch size warning.
            LogMemoryUsage("Start");
            if (dwgPaths.Length >= BATCH_SIZE_WARN)
            {
                FileLogger.Log(LOG_PREFIX,
                    $"CẢNH BÁO batch lớn: {dwgPaths.Length} files (ngưỡng={BATCH_SIZE_WARN}). " +
                    $"Memory peak có thể cao, nếu AutoCAD crash hãy chia batch nhỏ hơn.");
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
            LogMemoryUsage($"After Phase 1 ({prepared.Count} sideDb held)");

            // PHASE 2 — clone vào current doc (PHẢI UI thread).
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                foreach (var p in prepared) p.SideDb?.Dispose();
                throw new InvalidOperationException("Không có document đang hoạt động.");
            }

            var phase2Sw = Stopwatch.StartNew();
            double firstOffsetX = 0, lastOffsetX = 0;

            // Defensive try/finally: đảm bảo mọi sideDb dispose kể cả khi exception ngoài per-file catch
            // (vd UnlockAllLayersInDest / ComputeInitialOffsetX / ZoomExtents throw).
            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                {
                    // Unlock + thaw mọi layer trong dest trước khi clone.
                    // Tránh eLayerLocked khi WblockCloneObjects insert entity vào layer đang locked/frozen.
                    int unlockCount = UnlockAllLayersInDest(doc.Database);
                    if (unlockCount > 0)
                        FileLogger.Log(LOG_PREFIX, $"Đã unlock/thaw {unlockCount} layer trong dest trước khi clone.");

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
                            // `primaries` = IdPair IsPrimary (entity): cloned vs dropped.
                            // `symbols`   = IdPair !IsPrimary (layer/block/linetype/...): cloned vs ignored (trùng dest).
                            FileLogger.Log(LOG_PREFIX,
                                $"  Clone '{item.FileName}': topLevel={cloneStats.SrcIds} → " +
                                $"primaries [cloned={cloneStats.PrimaryCloned}, dropped={cloneStats.PrimaryNotCloned}], " +
                                $"symbols [cloned={cloneStats.SymbolsCloned}, ignored={cloneStats.SymbolsIgnored}], " +
                                $"transformed={cloneStats.Transformed} (failed={cloneStats.TransformFailed}), " +
                                $"dx={cloneStats.Dx:F2}, took={fileSw.ElapsedMilliseconds}ms");

                            // P0 — Silent drop detection: source có entity nhưng WblockClone trả 0 primary
                            // = toàn bộ entity bị discard (thường do DuplicateRecordCloning.Ignore + anon block conflict).
                            // Đánh dấu Failed, KHÔNG advance offsetX, KHÔNG count Success — tránh UI báo nhầm OK.
                            if (cloneStats.SrcIds > 0 && cloneStats.PrimaryCloned == 0)
                            {
                                FileLogger.Log(LOG_PREFIX,
                                    $"  CẢNH BÁO SILENT DROP: '{item.FileName}' có {cloneStats.SrcIds} entity source " +
                                    $"nhưng 0 entity được clone vào dest. " +
                                    $"Nguyên nhân: anonymous block definitions trong src conflict với dest → WblockClone discard. " +
                                    $"→ File count là FAILED, offsetX KHÔNG advance.");
                                result.FailCount++;
                                result.AddError(item.FileName,
                                    $"0 entity cloned — anonymous blocks conflict với dest template. " +
                                    $"Cần pre-rename anon blocks (xem P3 trong SESSION_LOG).");
                                item.Advanced = false;
                                item.EffectiveWidth = 0;
                                continue;
                            }

                            // Log vị trí/bbox mỗi A1/CAS_HEAD đã clone + transform vào dest.
                            foreach (var ki in cloneStats.KeepAsIsCloned)
                            {
                                FileLogger.Log(LOG_PREFIX,
                                    $"    [dest] {ki.BlockName} handle=0x{ki.DestHandleValue:X} " +
                                    $"pos=({ki.Position.X:F0},{ki.Position.Y:F0}) rot={ki.Rotation * 180 / Math.PI:F1}° " +
                                    $"bbox=[({ki.Bbox.MinPoint.X:F0},{ki.Bbox.MinPoint.Y:F0})-({ki.Bbox.MaxPoint.X:F0},{ki.Bbox.MaxPoint.Y:F0})]");
                            }

                            // Hướng A — A1-frame anchored layout:
                            //   File có A1: effectiveW = A1.Width_src (gap A1↔A1 = COLLECTION_GAP, ổn định).
                            //   File không A1: fallback bbox tổng (Extents.Width).
                            double contentW = item.HasExtents ? item.Width : 0;
                            double effectiveW;
                            string layoutMode;
                            if (item.HasA1)
                            {
                                effectiveW = item.A1MaxXSrc - item.A1MinXSrc;
                                layoutMode = "A1-frame";
                            }
                            else
                            {
                                effectiveW = contentW;
                                layoutMode = "bbox-fallback (no A1)";
                            }

                            double prevOffset = offsetX;
                            bool advance = item.HasExtents || cloneStats.KeepAsIsCloned.Count > 0;
                            if (advance)
                                offsetX += effectiveW + COLLECTION_GAP;

                            item.EffectiveWidth = effectiveW;
                            item.Advanced = advance;

                            FileLogger.Log(LOG_PREFIX,
                                $"    Layout: mode={layoutMode}, effective={effectiveW:F0}mm, " +
                                $"bbox-content={contentW:F0}mm, " +
                                $"overflow=[L:{item.LeftOverflowSrc:F0}/R:{item.RightOverflowSrc:F0}]mm");

                            result.SuccessCount++;
                            item.PlacedOffsetX = prevOffset;
                            lastOffsetX = offsetX;
                            FileLogger.Log(LOG_PREFIX,
                                $"  Collected OK: {item.FileName} placed at x={prevOffset:F2} → next offsetX={offsetX:F2}");
                        }
                        // Broader exception taxonomy — log thêm context để diagnose FATAL sau crash.
                        catch (OutOfMemoryException oom)
                        {
                            fileSw.Stop();
                            result.FailCount++;
                            result.AddError(item.FileName, "Out of memory — batch quá lớn, thử giảm số file hoặc chia nhỏ.");
                            FileLogger.Log(LOG_PREFIX,
                                $"FATAL OutOfMemory tại file '{item.FileName}' (index {i + 1}/{prepared.Count}): {oom.Message}");
                            LogMemoryUsage($"At OOM '{item.FileName}'");
                            // Dừng batch — memory đã critical, file kế chắc chắn cũng fail.
                            item.SideDb?.Dispose();
                            break;
                        }
                        catch (System.Runtime.InteropServices.SEHException sehEx)
                        {
                            fileSw.Stop();
                            result.FailCount++;
                            result.AddError(item.FileName, $"Native exception (SEH) trong AutoCAD: {sehEx.Message}");
                            FileLogger.Log(LOG_PREFIX,
                                $"FATAL SEHException tại file '{item.FileName}': ErrorCode=0x{sehEx.ErrorCode:X} — native code crash, file có thể corrupt.");
                            FileLogger.LogException(LOG_PREFIX, $"SEH {item.FileName}", sehEx);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception acEx)
                        {
                            fileSw.Stop();
                            result.FailCount++;
                            result.AddError(item.FileName, $"AutoCAD error: {acEx.ErrorStatus} — {acEx.Message}");
                            FileLogger.Log(LOG_PREFIX,
                                $"AutoCAD Exception tại file '{item.FileName}': ErrorStatus={acEx.ErrorStatus}");
                            FileLogger.LogException(LOG_PREFIX, $"AutoCAD {item.FileName}", acEx);
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
                            try { item.SideDb?.Dispose(); }
                            catch (Exception disposeEx)
                            {
                                FileLogger.Log(LOG_PREFIX, $"Dispose sideDb FAIL '{item.FileName}': {disposeEx.Message}");
                            }
                        }

                        // Memory check định kỳ — cảnh báo nếu vượt ngưỡng để user biết có thể sắp OOM.
                        long memMb = GC.GetTotalMemory(false) / (1024 * 1024);
                        if (memMb > MEMORY_WARN_MB)
                        {
                            FileLogger.Log(LOG_PREFIX,
                                $"  ⚠ Memory = {memMb}MB (ngưỡng={MEMORY_WARN_MB}MB) sau file {i + 1}/{prepared.Count}. " +
                                "Nếu tiếp tục tăng, nên dừng và collect batch nhỏ hơn.");
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
            }
            finally
            {
                // Defensive: dispose mọi sideDb còn lại kể cả đã dispose trước đó (idempotent).
                int orphanDisposed = 0;
                foreach (var p in prepared)
                {
                    try
                    {
                        if (p.SideDb != null && !p.SideDb.IsDisposed)
                        {
                            p.SideDb.Dispose();
                            orphanDisposed++;
                        }
                    }
                    catch { /* dispose fail — mất cleanup chuỗi, không block */ }
                }
                if (orphanDisposed > 0)
                    FileLogger.Log(LOG_PREFIX, $"Defensive cleanup: dispose {orphanDisposed} orphan sideDb(s) còn lại.");
            }
            phase2Sw.Stop();
            FileLogger.Log(LOG_PREFIX,
                $"--- PHASE 2 (clone) xong sau {phase2Sw.ElapsedMilliseconds}ms — " +
                $"success={result.SuccessCount}, failed={result.FailCount}.");
            LogMemoryUsage("After Phase 2");

            // Force GC để giải phóng sideDbs đã dispose — log pre/post cho user thấy memory leak (nếu có).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            LogMemoryUsage("After forced GC");

            totalSw.Stop();
            WriteFinalSummary(dwgPaths, prepared, result, firstOffsetX, lastOffsetX,
                phase1Sw.ElapsedMilliseconds, phase2Sw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds);

            progress?.Report($"Hoàn tất — Thành công: {result.SuccessCount}, Thất bại: {result.FailCount}");
            Debug.WriteLine($"{LOG_PREFIX} CollectDrawingsAsync HOÀN TẤT — {result.SuccessCount}/{dwgPaths.Length}.");
            return result;
        }

        #endregion

        #region Memory tracking

        /// <summary>
        /// Log memory snapshot hiện tại — managed heap (GC) + working set (OS process).
        /// Dùng tracking memory leak / OOM trigger point.
        /// `GC.GetTotalMemory(false)` không force collect → phản ánh state sau allocate, có thể cao hơn realistic memory in-use.
        /// </summary>
        private static void LogMemoryUsage(string label)
        {
            try
            {
                long managedMb = GC.GetTotalMemory(false) / (1024 * 1024);
                long workingSetMb;
                try
                {
                    using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        workingSetMb = proc.WorkingSet64 / (1024 * 1024);
                    }
                }
                catch { workingSetMb = -1; }

                FileLogger.Log("[Memory]",
                    $"{label}: managed={managedMb}MB, workingSet={(workingSetMb < 0 ? "N/A" : workingSetMb + "MB")}");
            }
            catch { /* logging memory không được fail silently */ }
        }

        #endregion
    }
}
