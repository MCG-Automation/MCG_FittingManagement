using System;
using System.IO;
using Microsoft.CSharp.RuntimeBinder;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Vault integration qua Inventor COM — mượn Vault AddIn đã cài trong Inventor để pull latest
    /// từ Vault server trước khi import file IDW vào thư viện.
    ///
    /// Approach (Phương án B):
    /// - Không reference Vault SDK riêng.
    /// - Dùng `ApplicationAddIns` của Inventor COM để tìm Vault AddIn.
    /// - Access `Automation` property (COM late-bound) để gọi Vault API.
    /// - Best-effort với nhiều method name fallback — Vault AddIn API không stable across versions.
    /// - Graceful degradation: nếu Vault không available / chưa login / file không trong Vault → log + skip,
    ///   proceed với file local. Không bao giờ fail toàn import flow vì Vault issue.
    /// </summary>
    public partial class FittingManagementService
    {
        #region Vault Refresh via Inventor AddIn

        /// <summary>
        /// Thử pull latest của 1 file IDW từ Vault qua Inventor AddIn.
        /// Gọi TRƯỚC khi Inventor mở file để extract — đảm bảo đọc từ disk là version mới nhất.
        /// </summary>
        /// <param name="invApp">Inventor application COM object (late-bound dynamic).</param>
        /// <param name="filePath">Đường dẫn local đến file .idw.</param>
        /// <returns>VaultRefreshResult — caller đọc Status để log/quyết định, không throw.</returns>
        private VaultRefreshResult TryPullLatestFromVault(dynamic invApp, string filePath)
        {
            FileLogger.Log(LOG_PREFIX, $"  Vault: thử pull latest '{Path.GetFileName(filePath)}'...");

            // 1. Tìm Vault AddIn trong ApplicationAddIns
            dynamic vaultAddIn = FindVaultAddIn(invApp);
            if (vaultAddIn == null)
                return LogAndReturn(VaultRefreshResult.SkippedNoAddIn("Không tìm thấy Vault AddIn trong Inventor (cài Vault Client Inventor Add-In trước)."));

            // 2. Check AddIn activated
            bool activated = false;
            try { activated = (bool)vaultAddIn.Activated; } catch { }
            if (!activated)
                return LogAndReturn(VaultRefreshResult.SkippedNoAddIn("Vault AddIn chưa Active — bật qua Inventor → Tools → Add-In Manager."));

            // 3. Lấy Automation object (COM interface Vault-specific)
            dynamic auto;
            try { auto = vaultAddIn.Automation; }
            catch (Exception ex)
            {
                return LogAndReturn(VaultRefreshResult.Failed(filePath, $"Không access được Vault AddIn.Automation: {ex.Message}"));
            }
            if (auto == null)
                return LogAndReturn(VaultRefreshResult.SkippedNoAddIn("Vault AddIn Automation = null."));

            // 4. Check login status — Vault API có property `LoggedIn` hoặc `IsLoggedIn` tùy version
            bool? loggedIn = TryGetLoggedInStatus(auto);
            if (loggedIn == false)
                return LogAndReturn(VaultRefreshResult.SkippedNotLoggedIn());
            if (loggedIn == null)
                FileLogger.Log(LOG_PREFIX, "  Vault: không xác định được login status, thử tiếp với assumption đã login...");

            // 5. Thử pull latest qua nhiều method name — Vault AddIn API không document chính thức,
            //    fallback chain để cover nhiều Inventor/Vault version.
            var tryMethods = new[]
            {
                "GetLatestServerVersion",   // Tên phổ biến Vault AddIn cũ
                "RefreshFile",              // Vault AddIn newer
                "GetLatestForDocument",
                "RefreshDocument",
                "GetLatest",
                "UpdateFile",
            };

            foreach (var methodName in tryMethods)
            {
                try
                {
                    // Dynamic invoke — nếu method không exist sẽ throw RuntimeBinderException
                    InvokeVaultMethod(auto, methodName, filePath);
                    return LogAndReturn(VaultRefreshResult.Success(filePath, methodName));
                }
                catch (RuntimeBinderException)
                {
                    // Method này không có — thử method kế
                    continue;
                }
                catch (Exception ex)
                {
                    // Method tồn tại nhưng throw (có thể do file không trong Vault, args sai signature...)
                    FileLogger.Log(LOG_PREFIX, $"  Vault.{methodName}('{Path.GetFileName(filePath)}') throw: {ex.Message}");

                    // Nếu message gợi ý "not in vault" hoặc "not found" → skip rõ hơn
                    string msg = ex.Message?.ToLowerInvariant() ?? "";
                    if (msg.Contains("not found") || msg.Contains("not in vault") || msg.Contains("does not exist"))
                        return LogAndReturn(VaultRefreshResult.SkippedNotInVault(filePath));

                    continue;
                }
            }

            return LogAndReturn(VaultRefreshResult.Failed(filePath,
                $"Không method Vault nào trong fallback chain hoạt động. Đã thử: {string.Join(", ", tryMethods)}"));
        }

        /// <summary>
        /// Iterate Inventor.ApplicationAddIns tìm AddIn có DisplayName chứa "Vault".
        /// Log danh sách AddIn để user biết có những gì available.
        /// </summary>
        private dynamic FindVaultAddIn(dynamic invApp)
        {
            try
            {
                dynamic addIns = invApp.ApplicationAddIns;
                int count = 0;
                try { count = (int)addIns.Count; } catch { }
                FileLogger.Log(LOG_PREFIX, $"  Vault: Inventor có {count} ApplicationAddIn(s).");

                foreach (dynamic addIn in addIns)
                {
                    string display = "";
                    try { display = (string)addIn.DisplayName ?? ""; } catch { }
                    if (display.IndexOf("Vault", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        FileLogger.Log(LOG_PREFIX, $"  Vault: Found AddIn '{display}'.");
                        return addIn;
                    }
                }
                FileLogger.Log(LOG_PREFIX, "  Vault: Không AddIn nào có tên chứa 'Vault'.");
            }
            catch (Exception ex)
            {
                FileLogger.Log(LOG_PREFIX, $"  Vault: FindVaultAddIn lỗi: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Thử đọc login status. Vault AddIn có thể expose property 'LoggedIn' hoặc 'IsLoggedIn'.
        /// Return true = logged in, false = not logged in, null = không xác định (property không có).
        /// </summary>
        private bool? TryGetLoggedInStatus(dynamic auto)
        {
            try { return (bool)auto.LoggedIn; } catch (RuntimeBinderException) { } catch { return null; }
            try { return (bool)auto.IsLoggedIn; } catch (RuntimeBinderException) { } catch { return null; }
            try { return (bool)auto.LoggedOn; } catch (RuntimeBinderException) { } catch { return null; }
            return null;
        }

        /// <summary>
        /// Dynamic invoke Vault API method. Tách ra để dễ try/catch per method.
        /// Một số method trả void, một số trả bool success — không quan tâm return value, chỉ quan tâm không throw.
        /// </summary>
        private void InvokeVaultMethod(dynamic auto, string methodName, string filePath)
        {
            // Reflection approach sẽ phức tạp với COM object. Dùng switch + dynamic dispatch.
            switch (methodName)
            {
                case "GetLatestServerVersion": auto.GetLatestServerVersion(filePath); break;
                case "RefreshFile":            auto.RefreshFile(filePath); break;
                case "GetLatestForDocument":   auto.GetLatestForDocument(filePath); break;
                case "RefreshDocument":        auto.RefreshDocument(filePath); break;
                case "GetLatest":              auto.GetLatest(filePath); break;
                case "UpdateFile":             auto.UpdateFile(filePath); break;
                default: throw new RuntimeBinderException($"Method {methodName} không map.");
            }
        }

        private VaultRefreshResult LogAndReturn(VaultRefreshResult r)
        {
            string fileLabel = string.IsNullOrEmpty(r.FilePath) ? "(no file)" : Path.GetFileName(r.FilePath);
            FileLogger.Log(LOG_PREFIX, $"  Vault result [{fileLabel}]: {r.Status} — {r.Message}");
            return r;
        }

        /// <summary>Label ngắn gọn cho progress text — 1-2 từ để fit trong TxtImportStatus single-line.</summary>
        internal static string StatusShortLabel(VaultRefreshStatus status)
        {
            switch (status)
            {
                case VaultRefreshStatus.Success: return "latest";
                case VaultRefreshStatus.AlreadyLatest: return "already latest";
                case VaultRefreshStatus.SkippedNoAddIn: return "skip (no AddIn)";
                case VaultRefreshStatus.SkippedNotLoggedIn: return "skip (not logged in)";
                case VaultRefreshStatus.SkippedNotInVault: return "skip (not in vault)";
                case VaultRefreshStatus.Failed: return "failed";
                default: return status.ToString();
            }
        }

        #endregion
    }
}
