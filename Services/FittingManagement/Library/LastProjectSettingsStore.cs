using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Ghi nhớ Project Folder gần nhất qua %APPDATA%\MCG_FittingManagement\lastproject.json — để
    /// <see cref="ActiveProjectContext"/> tự restore lại đúng project đang làm việc mỗi khi mở AutoCAD,
    /// không bắt user phải "Load Folder" lại mỗi phiên. Cùng convention thư mục với
    /// <see cref="Utilities.FileLogger"/> (%APPDATA%\MCG_FittingManagement\), tolerant với lỗi đọc/ghi
    /// (không throw ra ngoài — mất setting này không nghiêm trọng, chỉ cần Load Folder lại).
    /// </summary>
    internal static class LastProjectSettingsStore
    {
        private const string LOG_PREFIX = "[LastProjectSettingsStore]";

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MCG_FittingManagement", "lastproject.json");

        private class SettingsData
        {
            public string LastProjectFolder { get; set; }
        }

        /// <summary>Đọc lại Project Folder gần nhất — trả về null nếu chưa từng lưu hoặc lỗi đọc.</summary>
        public static string LoadLastFolder()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                string raw = File.ReadAllText(SettingsPath);
                if (string.IsNullOrWhiteSpace(raw)) return null;
                return JsonConvert.DeserializeObject<SettingsData>(raw)?.LastProjectFolder;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI LoadLastFolder: {ex.Message}");
                return null;
            }
        }

        /// <summary>Lưu lại Project Folder hiện tại — im lặng bỏ qua nếu ghi lỗi.</summary>
        public static void SaveLastFolder(string folder)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(new SettingsData { LastProjectFolder = folder }, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI SaveLastFolder: {ex.Message}");
            }
        }
    }
}
