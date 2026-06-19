using System;
using System.IO;

namespace MCG_FittingManagement.Utilities
{
    /// <summary>
    /// Logger ghi ra file tai %APPDATA%\MCG_FittingManagement\plugin.log.
    /// Khong can quyen Administrator, thread-safe.
    /// Dung thay the/bo sung cho Debug.WriteLine khi can debug ma khong co DebugView.
    /// </summary>
    public static class FileLogger
    {
        private static readonly object _lock = new object();
        private static readonly string _logPath;
        private const long MAX_LOG_SIZE = 5 * 1024 * 1024; // 5MB - tu xoay file khi vuot nguong

        /// <summary>Duong dan toi file log.</summary>
        public static string LogPath => _logPath;

        /// <summary>Duong dan toi thu muc chua log.</summary>
        public static string LogDirectory => Path.GetDirectoryName(_logPath);

        static FileLogger()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDir = Path.Combine(appDataPath, "MCG_FittingManagement");
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);
                _logPath = Path.Combine(logDir, "plugin.log");
            }
            catch
            {
                // Fallback ve TEMP neu APPDATA khong accessible
                _logPath = Path.Combine(Path.GetTempPath(), "MCG_FittingManagement_plugin.log");
            }
        }

        /// <summary>
        /// Ghi mot dong log voi prefix class va timestamp.
        /// </summary>
        public static void Log(string prefix, string message)
        {
            try
            {
                lock (_lock)
                {
                    RotateLogIfNeeded();
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string line = $"[{timestamp}] {prefix} {message}";
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging khong duoc gay crash app
            }
        }

        /// <summary>
        /// Ghi chi tiet exception (message + stack trace + inner).
        /// </summary>
        public static void LogException(string prefix, string context, Exception ex)
        {
            if (ex == null) return;
            Log(prefix, $"LOI {context}: {ex.GetType().Name}: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace))
                Log(prefix, $"Stack trace:\n{ex.StackTrace}");
            if (ex.InnerException != null)
                LogException(prefix, $"{context} (inner)", ex.InnerException);
        }

        /// <summary>
        /// Ghi header phan biet session/command. TRUNCATE log truoc khi ghi — moi session
        /// bat dau voi log rong de user de debug va share clean log khi bao loi.
        /// </summary>
        public static void LogSessionStart(string sessionName)
        {
            try
            {
                lock (_lock)
                {
                    // Truncate: tao file moi de lai, ghi de noi dung cu.
                    if (File.Exists(_logPath))
                    {
                        try { File.Delete(_logPath); } catch { /* file dang mo o tool khac - bo qua */ }
                    }

                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string header =
                        "========================================================" + Environment.NewLine +
                        $"[{timestamp}] === SESSION START: {sessionName} ===" + Environment.NewLine +
                        "========================================================" + Environment.NewLine;
                    File.AppendAllText(_logPath, header);
                }
            }
            catch { }
        }

        /// <summary>
        /// Xoa toan bo log file.
        /// </summary>
        public static void ClearLog()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(_logPath)) File.Delete(_logPath);
                }
            }
            catch { }
        }

        /// <summary>
        /// Xoay file khi vuot qua MAX_LOG_SIZE - rename thanh plugin.log.old, tao file moi.
        /// </summary>
        private static void RotateLogIfNeeded()
        {
            try
            {
                if (!File.Exists(_logPath)) return;
                var info = new FileInfo(_logPath);
                if (info.Length < MAX_LOG_SIZE) return;

                string oldPath = _logPath + ".old";
                if (File.Exists(oldPath)) File.Delete(oldPath);
                File.Move(_logPath, oldPath);
            }
            catch { }
        }
    }
}
