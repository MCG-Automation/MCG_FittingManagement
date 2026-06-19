using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities;

using VDF = Autodesk.DataManagement.Client.Framework;
using VltConn = Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections;
using VltEnt = Autodesk.DataManagement.Client.Framework.Vault.Currency.Entities;
using VltSettings = Autodesk.DataManagement.Client.Framework.Vault.Settings;
using WS = Autodesk.Connectivity.WebServices;
using VDFCurrency = Autodesk.DataManagement.Client.Framework.Currency;
using VltFormsSettings = Autodesk.DataManagement.Client.Framework.Vault.Forms.Settings;
using VltFormsLib = Autodesk.DataManagement.Client.Framework.Vault.Forms.Library;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// VDF SDK wrapper — pull latest file trực tiếp từ Vault server, không qua Inventor.
    /// KHÔNG thread-safe: 1 instance/batch import. Dispose khi xong để LogOut session.
    /// </summary>
    public class VaultDirectService : IVaultDirectService
    {
        private const string LOG_PREFIX = "[VaultDirectService]";

        // Static ctor install AssemblyResolve hook TRƯỚC khi JIT resolve VDF types ở method body.
        // Guaranteed chạy 1 lần per AppDomain, trước new VaultDirectService() hoặc static member access.
        static VaultDirectService()
        {
            VaultAssemblyResolver.Install();
        }

        private VltConn.Connection _connection;
        private bool _disposed;

        public bool IsSignedIn => _connection != null && _connection.IsConnected && !_disposed;

        public VaultConnectionInfo AutoDetectConnection()
        {
            FileLogger.Log(LOG_PREFIX, "Đang auto-detect Vault connection từ LoginHistory.xml...");

            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // Probe 2 location — tuỳ Vault version file ở ngay VaultCommon\ hoặc VaultCommon\Servers\.
                string[] candidates =
                {
                    Path.Combine(appData, "Autodesk", "VaultCommon", "LoginHistory.xml"),
                    Path.Combine(appData, "Autodesk", "VaultCommon", "Servers", "LoginHistory.xml"),
                };

                string historyPath = null;
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { historyPath = c; break; }
                }

                if (historyPath == null)
                {
                    FileLogger.Log(LOG_PREFIX,
                        $"  Không tìm thấy LoginHistory.xml (đã thử: {string.Join(" | ", candidates)}). " +
                        "User cần mở Vault Explorer login ít nhất 1 lần để tạo file này.");
                    return null;
                }

                FileLogger.Log(LOG_PREFIX, $"  Tìm thấy LoginHistory.xml: {historyPath}");

                XDocument doc = XDocument.Load(historyPath);
                XElement root = doc.Root;
                if (root == null) return null;

                string lastServer = GetLocalName(root, "LastServerName");
                string lastVault = GetLocalName(root, "LastVault");
                string lastUser = GetLocalName(root, "LastUserName");

                if (string.IsNullOrWhiteSpace(lastServer) || string.IsNullOrWhiteSpace(lastVault))
                {
                    FileLogger.Log(LOG_PREFIX, "  LoginHistory.xml không có LastServerName/LastVault.");
                    return null;
                }

                var info = new VaultConnectionInfo
                {
                    Server = lastServer.Trim(),
                    Vault = lastVault.Trim(),
                    UserName = lastUser?.Trim() ?? ""
                };
                FileLogger.Log(LOG_PREFIX, $"  Auto-detect OK: {info}");
                return info;
            }
            catch (Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "AutoDetectConnection", ex);
                return null;
            }
        }

        public bool EnsureSignedIn(VaultConnectionInfo conn)
        {
            if (IsSignedIn)
            {
                FileLogger.Log(LOG_PREFIX, "  EnsureSignedIn: đã sign-in, bỏ qua.");
                return true;
            }

            if (conn == null || !conn.IsValid)
            {
                FileLogger.Log(LOG_PREFIX, "  EnsureSignedIn FAIL: VaultConnectionInfo không hợp lệ.");
                return false;
            }

            try
            {
                FileLogger.Log(LOG_PREFIX, $"  Đang sign-in: {conn}...");
                FileLogger.Log(LOG_PREFIX,
                    $"  Current thread: ApartmentState={System.Threading.Thread.CurrentThread.GetApartmentState()}, " +
                    $"IsBackground={System.Threading.Thread.CurrentThread.IsBackground}, " +
                    $"ID={System.Threading.Thread.CurrentThread.ManagedThreadId}.");

                var settings = new VltFormsSettings.LoginSettings
                {
                    ServerName = conn.Server,
                    VaultName = conn.Vault,
                    // RestoreAndExecute: silent nếu có cached session, bật dialog nếu không.
                    AutoLoginMode = VltFormsSettings.LoginSettings.AutoLoginModeValues.RestoreAndExecute
                };
                FileLogger.Log(LOG_PREFIX,
                    $"  Settings: ServerName={settings.ServerName}, VaultName={settings.VaultName}, AutoLoginMode={settings.AutoLoginMode}.");

                // Library.Login(LoginSettings) → Connection (null nếu user cancel / auto-login fail).
                _connection = VltFormsLib.Login(settings);

                if (_connection == null)
                {
                    FileLogger.Log(LOG_PREFIX,
                        "  Sign-in FAIL: Library.Login trả null. Nguyên nhân có thể: " +
                        "(a) user cancel dialog, " +
                        "(b) auto-login silent mode không tìm được cached creds và dialog không bật (check STA thread), " +
                        "(c) server unreachable.");
                    return false;
                }

                if (!_connection.IsConnected)
                {
                    FileLogger.Log(LOG_PREFIX,
                        $"  Sign-in FAIL: Connection object không null nhưng IsConnected=false. Server={_connection.Server}, Vault={_connection.Vault}.");
                    _connection = null;
                    return false;
                }

                FileLogger.Log(LOG_PREFIX,
                    $"  ✓ Sign-in OK — {_connection.Server}/{_connection.Vault}, User={_connection.UserName} (ID={_connection.UserID}).");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "EnsureSignedIn", ex);
                _connection = null;
                return false;
            }
        }

        public VaultRefreshResult DownloadLatest(string fileName, string localPath)
        {
            if (!IsSignedIn)
                return VaultRefreshResult.SkippedNotLoggedIn();

            if (string.IsNullOrWhiteSpace(fileName))
                return VaultRefreshResult.Failed(localPath, "fileName rỗng.");

            try
            {
                FileLogger.Log(LOG_PREFIX, $"  Tìm file '{fileName}' trong Vault...");

                var docSvc = _connection.WebServiceManager.DocumentService;

                // PropDefId của "ClientFileName" (user-visible filename) — lấy dynamic để không hardcode.
                long fileNamePropId = ResolveFileNamePropDefId();
                if (fileNamePropId == 0)
                    return VaultRefreshResult.Failed(localPath, "Không tìm được PropDefId của ClientFileName.");

                var cond = new WS.SrchCond
                {
                    PropDefId = fileNamePropId,
                    PropTyp = WS.PropertySearchType.SingleProperty,
                    SrchOper = 1, // Equals (case-insensitive theo default Vault)
                    SrchTxt = fileName,
                    SrchRule = WS.SearchRuleType.Must
                };

                string bookmark = string.Empty;
                WS.SrchStatus status;
                var files = docSvc.FindFilesBySearchConditions(
                    new[] { cond },
                    null,
                    null,
                    true,   // recurseFolders
                    true,   // latestOnly
                    ref bookmark,
                    out status);

                if (files == null || files.Length == 0)
                {
                    FileLogger.Log(LOG_PREFIX, $"  File '{fileName}' không có trong Vault.");
                    return VaultRefreshResult.SkippedNotInVault(localPath);
                }

                // Exact-match theo tên (tránh partial match nếu search trả fuzzy).
                WS.File match = files.FirstOrDefault(f =>
                    string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    ?? files[0];

                FileLogger.Log(LOG_PREFIX,
                    $"  Found: {match.Name} (MasterId={match.MasterId}, Ver={match.VerNum}, Size={match.FileSize}B). Đang download...");

                // Build FileIteration + AcquireFilesSettings → FileManager.AcquireFiles
                var fileIter = new VltEnt.FileIteration(_connection, match);

                var acqSettings = new VltSettings.AcquireFilesSettings(_connection, false);
                acqSettings.AddFileToAcquire(
                    fileIter,
                    VltSettings.AcquireFilesSettings.AcquisitionOption.Download,
                    new VDFCurrency.FilePathAbsolute(localPath));

                if (!acqSettings.IsValidConfiguration())
                    return VaultRefreshResult.Failed(localPath, "AcquireFilesSettings.IsValidConfiguration=false.");

                var results = _connection.FileManager.AcquireFiles(acqSettings);
                if (results == null || results.IsCancelled)
                    return VaultRefreshResult.Failed(localPath, "AcquireFiles bị cancel.");

                if (!File.Exists(localPath))
                    return VaultRefreshResult.Failed(localPath,
                        $"AcquireFiles hoàn tất nhưng file không xuất hiện tại {localPath}.");

                var info = new FileInfo(localPath);
                FileLogger.Log(LOG_PREFIX, $"  ✓ Downloaded {info.Length} bytes → {localPath}.");
                return VaultRefreshResult.Success(localPath, $"VDF:AcquireFiles(v{match.VerNum})");
            }
            catch (Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, $"DownloadLatest '{fileName}'", ex);
                return VaultRefreshResult.Failed(localPath, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // KHÔNG gọi Library.Logout ở đây — VDF sẽ xoá cached session, làm import kế tiếp
            // phải sign-in lại (nhanh qua SSO nhưng vẫn tốn 1-2s + risk prompt user).
            // Chỉ release local reference; VDF giữ session sống đến khi AutoCAD.exe thoát.
            if (_connection != null)
            {
                FileLogger.Log(LOG_PREFIX, "Dispose — giữ Vault session sống cho import kế tiếp (không logout).");
                _connection = null;
            }
        }

        #region Helpers

        private static string GetLocalName(XElement root, string localName)
        {
            foreach (var e in root.Elements())
            {
                if (string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal))
                    return (string)e;
            }
            return null;
        }

        // Cached để lookup 1 lần per session (call cost ~100-300ms).
        private long _cachedFileNamePropDefId;

        /// <summary>
        /// Resolve PropDefId của property "ClientFileName" (hệ thống). Cache trong session.
        /// Dùng PropertyDefinitionService.GetPropertyDefinitionsByEntityClassId("FILE").
        /// </summary>
        private long ResolveFileNamePropDefId()
        {
            if (_cachedFileNamePropDefId != 0) return _cachedFileNamePropDefId;

            try
            {
                var propSvc = _connection.WebServiceManager.PropertyService;
                WS.PropDef[] defs = propSvc.GetPropertyDefinitionsByEntityClassId("FILE");
                var clientFileName = defs.FirstOrDefault(d =>
                    string.Equals(d.SysName, "ClientFileName", StringComparison.OrdinalIgnoreCase));
                if (clientFileName != null)
                {
                    _cachedFileNamePropDefId = clientFileName.Id;
                    FileLogger.Log(LOG_PREFIX, $"  Resolved ClientFileName PropDefId={clientFileName.Id}.");
                    return clientFileName.Id;
                }

                // Fallback: common hardcoded id 4 (Vault 2012+ default schema).
                FileLogger.Log(LOG_PREFIX, "  CẢNH BÁO: không tìm thấy 'ClientFileName', fallback PropDefId=4.");
                _cachedFileNamePropDefId = 4;
                return 4;
            }
            catch (Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "ResolveFileNamePropDefId", ex);
                return 0;
            }
        }

        #endregion
    }
}
