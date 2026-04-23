using System;
using System.IO;
using System.Reflection;

namespace MCGCadPlugin.Utilities
{
    /// <summary>
    /// Resolver assembly cho Autodesk Vault SDK (VDF) DLLs.
    /// VDF DLLs KHÔNG nằm trong GAC — plugin reference với Private=False nên runtime phải
    /// tự load từ install folder của Vault Client. Probe nhiều version (2026 → 2020).
    ///
    /// Install() ở static ctor của plugin entry point — 1 lần per AppDomain.
    /// </summary>
    public static class VaultAssemblyResolver
    {
        private const string LOG_PREFIX = "[VaultAssemblyResolver]";

        private static readonly string[] VaultVersions =
        {
            "2026", "2025", "2024", "2023", "2022", "2021", "2020"
        };

        // Chỉ probe assembly có prefix này — tránh hook vào load của các assembly khác.
        private static readonly string[] VaultAssemblyPrefixes =
        {
            "Autodesk.Connectivity",
            "Autodesk.DataManagement",
            "Connectivity.Application",
            "Connectivity.Branding",
            "Connectivity.ClientShared",
        };

        private static bool _installed;
        private static string _resolvedVaultDir;

        /// <summary>
        /// Install AssemblyResolve hook. Gọi 1 lần ở plugin entry point (trước khi call VDF API).
        /// Idempotent — gọi nhiều lần chỉ install 1 hook duy nhất.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            FileLogger.Log(LOG_PREFIX, "Đã install AssemblyResolve hook cho Vault SDK.");
        }

        /// <summary>
        /// Trả về đường dẫn thư mục Vault Client đã detect (hoặc null nếu chưa detect).
        /// Chỉ có giá trị sau lần resolve đầu tiên success.
        /// </summary>
        public static string ResolvedVaultDirectory => _resolvedVaultDir;

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string assemblyName;
            try { assemblyName = new AssemblyName(args.Name).Name; }
            catch { return null; }

            if (!IsVaultAssembly(assemblyName)) return null;

            // Đã detect path → load thẳng từ đó (fast path).
            if (!string.IsNullOrEmpty(_resolvedVaultDir))
            {
                string cachedPath = Path.Combine(_resolvedVaultDir, assemblyName + ".dll");
                if (File.Exists(cachedPath))
                {
                    return SafeLoadFrom(cachedPath);
                }
            }

            // Chưa detect — probe các version Vault từ mới nhất xuống cũ nhất.
            foreach (string version in VaultVersions)
            {
                string probeDir = $@"C:\Program Files\Autodesk\Vault Client {version}\Explorer";
                string dllPath = Path.Combine(probeDir, assemblyName + ".dll");
                if (!File.Exists(dllPath)) continue;

                _resolvedVaultDir = probeDir;
                FileLogger.Log(LOG_PREFIX,
                    $"Đã detect Vault Client {version} tại: {probeDir}");
                return SafeLoadFrom(dllPath);
            }

            // `.resources` (localization satellite) và `.XmlSerializers` (serialization helper cache)
            // là file OPTIONAL của .NET — trả null = "dùng default", không phải lỗi.
            // Chỉ log warning cho DLL core thực sự cần.
            if (assemblyName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.EndsWith(".XmlSerializers", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            FileLogger.Log(LOG_PREFIX,
                $"CẢNH BÁO: Không tìm thấy '{assemblyName}.dll' trong bất kỳ Vault Client version nào (2020-2026). " +
                "Vault SDK không load được — Vault refresh sẽ fail.");
            return null;
        }

        private static bool IsVaultAssembly(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string prefix in VaultAssemblyPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static Assembly SafeLoadFrom(string path)
        {
            try
            {
                Assembly asm = Assembly.LoadFrom(path);
                FileLogger.Log(LOG_PREFIX, $"Loaded: {path}");
                return asm;
            }
            catch (Exception ex)
            {
                FileLogger.Log(LOG_PREFIX, $"LỖI LoadFrom '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
