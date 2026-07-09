using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Utilities.FittingManagement
{
    /// <summary>
    /// Helper đọc/ghi/merge JSON catalog (Master và Project dùng chung định dạng).
    /// Mọi I/O JSON cho catalog phải đi qua class này — tránh mỗi nơi tự deserialize lệch logic.
    /// </summary>
    public static class CatalogJsonStore
    {
        private const string LOG_PREFIX = "[CatalogJsonStore]";

        /// <summary>Đọc catalog dạng <typeparamref name="T"/>. Trả về list rỗng nếu file thiếu / parse fail.</summary>
        public static List<T> Read<T>(string jsonPath) where T : CatalogItem
        {
            if (!File.Exists(jsonPath))
            {
                Debug.WriteLine($"{LOG_PREFIX} File không tồn tại: {jsonPath}");
                return new List<T>();
            }
            try
            {
                string raw = File.ReadAllText(jsonPath);
                if (string.IsNullOrWhiteSpace(raw)) return new List<T>();
                return JsonConvert.DeserializeObject<List<T>>(raw) ?? new List<T>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI Read({jsonPath}): {ex.Message}");
                return new List<T>();
            }
        }

        /// <summary>Ghi đè toàn bộ catalog xuống file JSON (formatting indented).</summary>
        public static void Write<T>(string jsonPath, IEnumerable<T> items) where T : CatalogItem
        {
            string folder = Path.GetDirectoryName(jsonPath);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(items ?? Enumerable.Empty<T>(), Formatting.Indented));
        }

        /// <summary>
        /// Merge danh sách item vào file JSON theo BlockName (case-sensitive như behavior cũ).
        /// Trả về Tuple(số mới, số cập nhật-revision-khác).
        /// </summary>
        public static Tuple<int, int> MergeItems(string jsonPath, IEnumerable<CatalogItem> newItems)
        {
            if (newItems == null) return new Tuple<int, int>(0, 0);

            List<CatalogItem> catalog = Read<CatalogItem>(jsonPath);
            int newCount = 0;
            int updatedCount = 0;

            foreach (var newItem in newItems)
            {
                var existing = catalog.FirstOrDefault(x => x.BlockName == newItem.BlockName);
                if (existing == null)
                {
                    newCount++;
                    catalog.Add(newItem);
                }
                else
                {
                    if (existing.Revision != newItem.Revision) updatedCount++;
                    // Giữ nguyên CreatedDate gốc — newItem thường được build mới (không mang theo
                    // CreatedDate) nên phải copy forward từ bản ghi cũ, tránh mất ngày khởi tạo mỗi lần merge.
                    if (string.IsNullOrEmpty(newItem.CreatedDate)) newItem.CreatedDate = existing.CreatedDate;
                    catalog.Remove(existing);
                    catalog.Add(newItem);
                }
            }

            Write(jsonPath, catalog);
            return new Tuple<int, int>(newCount, updatedCount);
        }

        /// <summary>Xoá toàn bộ item trong file JSON khớp BlockName trong <paramref name="blockNames"/>. Trả về số dòng bị xoá.</summary>
        public static int RemoveItems(string jsonPath, IEnumerable<string> blockNames)
        {
            if (blockNames == null) return 0;
            var nameSet = new HashSet<string>(blockNames, StringComparer.Ordinal);
            if (nameSet.Count == 0) return 0;

            List<CatalogItem> catalog = Read<CatalogItem>(jsonPath);
            int before = catalog.Count;
            catalog = catalog.Where(c => !nameSet.Contains(c.BlockName)).ToList();
            int removed = before - catalog.Count;
            if (removed > 0) Write(jsonPath, catalog);
            return removed;
        }
    }
}
