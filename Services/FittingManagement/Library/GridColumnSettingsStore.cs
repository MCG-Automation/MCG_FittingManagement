using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Ghi nhớ cấu hình cột hiển thị (Customize Grid) của Fitting Table qua
    /// %APPDATA%\MCG_FittingManagement\gridcolumns.json — dùng CHUNG cho mọi Project Folder (không
    /// phải riêng từng project, theo xác nhận của user). Chỉ lưu những cột user ĐÃ TỪNG toggle (không
    /// phải toàn bộ danh sách cột) — cột chưa từng toggle dùng default (cột cố định = hiện, cột động
    /// từ iProperty/Vault = ẩn). Cùng convention/độ tolerant với <see cref="LastProjectSettingsStore"/>.
    /// </summary>
    internal static class GridColumnSettingsStore
    {
        private const string LOG_PREFIX = "[GridColumnSettingsStore]";

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MCG_FittingManagement", "gridcolumns.json");

        private class SettingsData
        {
            public Dictionary<string, bool> ColumnVisibility { get; set; }
        }

        /// <summary>Đọc lại cấu hình cột đã lưu — trả về Dictionary rỗng nếu chưa từng lưu hoặc lỗi đọc.</summary>
        public static Dictionary<string, bool> LoadVisibility()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new Dictionary<string, bool>();
                string raw = File.ReadAllText(SettingsPath);
                if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, bool>();
                return JsonConvert.DeserializeObject<SettingsData>(raw)?.ColumnVisibility ?? new Dictionary<string, bool>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI LoadVisibility: {ex.Message}");
                return new Dictionary<string, bool>();
            }
        }

        /// <summary>Lưu lại cấu hình cột hiện tại — im lặng bỏ qua nếu ghi lỗi.</summary>
        public static void SaveVisibility(Dictionary<string, bool> overrides)
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(new SettingsData { ColumnVisibility = overrides }, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI SaveVisibility: {ex.Message}");
            }
        }
    }
}
