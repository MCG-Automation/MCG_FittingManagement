using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// IDW Import — entry point + orchestration. Phase 1 (Inventor COM) chạy trên worker thread,
    /// Phase 2 (AutoCAD db, ở <c>FittingManagementService.JsonImport.cs</c>) chạy main thread.
    ///
    /// File này chỉ giữ flow chính. Các phần con tách ra file partial cùng folder:
    /// <list type="bullet">
    ///   <item><c>.Inventor.cs</c> — Acquire/Release Inventor COM + per-file processing.</item>
    ///   <item><c>.Metadata.cs</c> — Trích iProperties + format Mass.</item>
    ///   <item><c>.Views.cs</c> — Trích DrawingViews + phân loại 2D/3D.</item>
    ///   <item><c>.DwgExport.cs</c> — Export sang DWG qua SaveAs hoặc Translator+INI.</item>
    /// </list>
    /// </summary>
    public partial class FittingManagementService
    {
        /// <summary>
        /// Gói dữ liệu của 1 file IDW đã extract thành công, dùng làm input cho bước split-view.
        /// </summary>
        private class ExtractedIdw
        {
            public string SourceIdwName { get; set; }
            public string DwgPath { get; set; }
            public FittingMetadata Metadata { get; set; }
        }

        /// <summary>
        /// Luồng gộp async: Phase 1 (Inventor COM — chậm) chạy trên worker thread qua <see cref="Task.Run(Action)"/>
        /// để UI AutoCAD không bị khoá; Phase 2 (AutoCAD db — cần main thread) chạy lại trên thread gốc
        /// của caller sau `await`. Báo tiến độ qua <paramref name="progress"/> nếu có.
        /// </summary>
        /// <param name="idwPaths">Danh sách đường dẫn file .idw</param>
        /// <param name="bomType">"PANEL" hoặc "DETAIL"</param>
        /// <param name="progress">Callback nhận thông điệp trạng thái (có thể null).</param>
        /// <returns>ImportResult tính theo file IDW (thành công khi extract OK và tạo được ≥1 block)</returns>
        public async Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, bool pullFromVault = false, IProgress<string> progress = null)
        {
            var result = new ImportResult();

            FileLogger.LogSessionStart($"ImportIdwFilesAsync ({idwPaths.Length} files, BomType={bomType}, pullFromVault={pullFromVault})");
            FileLogger.Log(LOG_PREFIX, $"Bắt đầu ImportIdwFilesAsync — {idwPaths.Length} file(s), BomType={bomType}, pullFromVault={pullFromVault}...");
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu ImportIdwFilesAsync — {idwPaths.Length} file(s), BomType={bomType}, pullFromVault={pullFromVault}...");

            // PHASE 0 — Vault sign-in TRÊN UI THREAD (trước Task.Run).
            // VDF Library.Login có thể bật dialog WinForms → phải ở STA/UI thread, không dùng worker thread.
            // Download từng file (WebService call) thì OK trên worker thread → sẽ gọi trong ExtractAllIdw.
            IVaultDirectService vaultService = null;
            if (pullFromVault)
            {
                progress?.Report("Signing in to Vault...");
                vaultService = new VaultDirectService();
                var conn = vaultService.AutoDetectConnection();
                if (conn == null || !vaultService.EnsureSignedIn(conn))
                {
                    // Sign-in fail → dispose, set null. Per-file sẽ ghi SkippedNotLoggedIn.
                    FileLogger.Log(LOG_PREFIX, "  Vault sign-in FAIL → skip Vault refresh cho cả batch.");
                    vaultService.Dispose();
                    vaultService = null;
                }
                else
                {
                    progress?.Report($"Signed in: {conn}");
                }
            }

            try
            {
                // PHASE 1 — worker thread: Inventor COM (Open/SaveAs/Close — chậm) + Vault download per file.
                progress?.Report($"Extracting {idwPaths.Length} IDW file(s) via Inventor...");
                var extractedItems = await Task.Run(() =>
                    ExtractAllIdw(idwPaths, result, progress, pullFromVault, vaultService));

                // PHASE 2 — thread gốc: AutoCAD db phải chạy trên main thread
                if (extractedItems.Count > 0)
                {
                    progress?.Report($"Creating blocks from {extractedItems.Count} DWG(s)...");
                    CreateBlocksFromExtracted(extractedItems, bomType, result, progress);
                }
            }
            finally
            {
                vaultService?.Dispose();
            }

            progress?.Report($"Done. Success={result.SuccessCount}, Failed={result.FailCount}");
            FileLogger.Log(LOG_PREFIX, $"HOÀN TẤT ImportIdwFilesAsync — Thành công: {result.SuccessCount}, Thất bại: {result.FailCount}.");
            Debug.WriteLine($"{LOG_PREFIX} HOÀN TẤT ImportIdwFilesAsync — Thành công: {result.SuccessCount}, Thất bại: {result.FailCount}.");
            return result;
        }

        /// <summary>
        /// PHASE 1 — Extract toàn bộ IDW. File extract fail bị ghi vào result.Errors ngay;
        /// file extract OK được trả về để PHASE 2 dùng. Chạy trên worker thread.
        /// Nếu <paramref name="pullFromVault"/> = true và <paramref name="vaultService"/> != null → download latest từ Vault trước khi extract.
        /// Nếu pullFromVault=true nhưng vaultService=null → sign-in đã fail, ghi SkippedNotLoggedIn cho mỗi file.
        /// </summary>
        private List<ExtractedIdw> ExtractAllIdw(string[] idwPaths, ImportResult result, IProgress<string> progress,
            bool pullFromVault, IVaultDirectService vaultService)
        {
            var extracted = new List<ExtractedIdw>();
            bool weStartedInventor = false;
            dynamic invApp = null;

            try
            {
                invApp = AcquireInventorInstance(out weStartedInventor);

                if (!Directory.Exists(_libraryFolderPath))
                {
                    Directory.CreateDirectory(_libraryFolderPath);
                    FileLogger.Log(LOG_PREFIX, $"Đã tạo thư mục: {_libraryFolderPath}");
                }

                int total = idwPaths.Length;
                for (int i = 0; i < total; i++)
                {
                    string idwPath = idwPaths[i];
                    string fileName = Path.GetFileName(idwPath);

                    // Pull latest từ Vault TRƯỚC extract — đảm bảo file local là latest trước khi Inventor đọc.
                    // Graceful degradation: Vault fail không fail toàn file; proceed extract file local.
                    if (pullFromVault)
                    {
                        Models.FittingManagement.VaultRefreshResult vaultResult;

                        if (vaultService == null)
                        {
                            // Sign-in đã fail từ Phase 0 — clone SkippedNotLoggedIn cho từng file.
                            vaultResult = Models.FittingManagement.VaultRefreshResult.SkippedNotLoggedIn();
                            vaultResult.FilePath = idwPath;
                        }
                        else
                        {
                            progress?.Report($"[{i + 1}/{total}] Vault download: {fileName}");
                            try
                            {
                                vaultResult = vaultService.DownloadLatest(fileName, idwPath);
                                if (vaultResult.Status == Models.FittingManagement.VaultRefreshStatus.Failed)
                                    FileLogger.Log(LOG_PREFIX, $"  Vault download fail (non-fatal) cho '{fileName}': {vaultResult.Message}");
                            }
                            catch (System.Exception vex)
                            {
                                FileLogger.LogException(LOG_PREFIX, $"VaultDirectService.DownloadLatest '{fileName}' (non-fatal)", vex);
                                vaultResult = Models.FittingManagement.VaultRefreshResult.Failed(idwPath, vex.Message);
                            }
                            if (string.IsNullOrEmpty(vaultResult.FilePath))
                                vaultResult.FilePath = idwPath;
                        }

                        result.VaultResults.Add(vaultResult);

                        // Realtime progress với status icon.
                        string icon = vaultResult.IsSuccess ? "✓" :
                                      vaultResult.Status == Models.FittingManagement.VaultRefreshStatus.Failed ? "✗" : "⚠";
                        string statusLabel = VaultStatusShortLabel(vaultResult.Status);
                        progress?.Report($"[{i + 1}/{total}] {icon} Vault {statusLabel}: {fileName}");
                    }

                    progress?.Report($"[{i + 1}/{total}] Extracting: {fileName}");

                    try
                    {
                        ExtractedIdw item = ProcessSingleIdwFile(invApp, idwPath);
                        extracted.Add(item);
                        FileLogger.Log(LOG_PREFIX, $"Extract IDW OK: {fileName} → {item.Metadata.Views?.Count ?? 0} view(s)");
                    }
                    catch (System.Exception ex)
                    {
                        result.FailCount++;
                        FileLogger.LogException(LOG_PREFIX, $"extract IDW '{fileName}'", ex);
                        result.AddError(fileName, $"Extract: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "ExtractAllIdw (outer)", ex);
                throw;
            }
            finally
            {
                ReleaseInventorInstance(invApp, weStartedInventor);
            }

            return extracted;
        }

        /// <summary>Label ngắn gọn cho progress text — 1-2 từ để fit single-line UI status.</summary>
        private static string VaultStatusShortLabel(Models.FittingManagement.VaultRefreshStatus status)
        {
            switch (status)
            {
                case Models.FittingManagement.VaultRefreshStatus.Success: return "updated";
                case Models.FittingManagement.VaultRefreshStatus.AlreadyLatest: return "already latest";
                case Models.FittingManagement.VaultRefreshStatus.SkippedNoAddIn: return "SDK missing";
                case Models.FittingManagement.VaultRefreshStatus.SkippedNotLoggedIn: return "not signed in";
                case Models.FittingManagement.VaultRefreshStatus.SkippedNotInVault: return "not in Vault";
                case Models.FittingManagement.VaultRefreshStatus.Failed: return "failed";
                default: return status.ToString();
            }
        }
    }
}
