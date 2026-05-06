using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using MCGCadPlugin.Models.FittingManagement;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Theo dõi BlockName của các item vừa được dùng — mỗi window 1 instance, mỗi instance 1 file JSON.
    /// FIFO 15 entries, sort theo timestamp giảm dần (mới nhất lên đầu).
    /// </summary>
    public class RecentItemsTracker : IRecentItemsTracker
    {
        private const string LOG_PREFIX = "[RecentItemsTracker]";
        private const int MAX_ENTRIES = 15;

        private readonly string _storeFilePath;

        /// <summary>Khởi tạo tracker với store file riêng (Master/Project khác nhau).</summary>
        public RecentItemsTracker(string storeFilePath)
        {
            _storeFilePath = storeFilePath;
            Debug.WriteLine($"{LOG_PREFIX} Khởi tạo với store: {_storeFilePath}");
        }

        /// <summary>Đẩy <paramref name="blockName"/> lên đầu list — nếu đã tồn tại thì cập nhật timestamp.</summary>
        public void Track(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName)) return;
            if (string.IsNullOrWhiteSpace(_storeFilePath)) return;

            try
            {
                var entries = ReadAll();
                entries.RemoveAll(e => string.Equals(e.BlockName, blockName, StringComparison.Ordinal));
                entries.Insert(0, new RecentItemEntry
                {
                    BlockName = blockName,
                    TimestampUtc = DateTime.UtcNow.Ticks
                });

                if (entries.Count > MAX_ENTRIES)
                    entries = entries.Take(MAX_ENTRIES).ToList();

                WriteAll(entries);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI Track({blockName}): {ex.Message}");
            }
        }

        /// <summary>Đọc danh sách BlockName, đã sort theo timestamp giảm dần.</summary>
        public List<string> GetRecentBlockNames()
        {
            try
            {
                return ReadAll()
                    .OrderByDescending(e => e.TimestampUtc)
                    .Select(e => e.BlockName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI GetRecentBlockNames: {ex.Message}");
                return new List<string>();
            }
        }

        #region Private I/O

        private List<RecentItemEntry> ReadAll()
        {
            if (string.IsNullOrWhiteSpace(_storeFilePath) || !File.Exists(_storeFilePath))
                return new List<RecentItemEntry>();

            string raw = File.ReadAllText(_storeFilePath);
            if (string.IsNullOrWhiteSpace(raw)) return new List<RecentItemEntry>();

            return JsonConvert.DeserializeObject<List<RecentItemEntry>>(raw) ?? new List<RecentItemEntry>();
        }

        private void WriteAll(List<RecentItemEntry> entries)
        {
            string folder = Path.GetDirectoryName(_storeFilePath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(_storeFilePath, JsonConvert.SerializeObject(entries, Formatting.Indented));
        }

        #endregion
    }
}
