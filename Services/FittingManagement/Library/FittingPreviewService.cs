using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// In-memory preview cache. Key = FilePath; Value = (lastWriteUtc, BitmapSource).
    /// Khi file .dwg newer hơn cache → re-extract.
    /// </summary>
    public class FittingPreviewService : IFittingPreviewService
    {
        private const string LOG_PREFIX = "[FittingPreviewService]";

        private class CacheEntry
        {
            public DateTime LastWriteUtc;
            public BitmapSource Preview;
        }

        private readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public BitmapSource GetPreview(CatalogItem item)
        {
            if (item == null) return null;
            if (!IsBlockType(item)) return null;

            string path = item.FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            DateTime fileWrite = File.GetLastWriteTimeUtc(path);

            if (_cache.TryGetValue(path, out CacheEntry hit) && hit.LastWriteUtc == fileWrite)
            {
                return hit.Preview;
            }

            var bmp = DwgThumbnailExtractor.Extract(path, item.BlockName);
            _cache[path] = new CacheEntry { LastWriteUtc = fileWrite, Preview = bmp };

            Debug.WriteLine($"{LOG_PREFIX} Cached preview cho {item.BlockName}: {(bmp != null ? "OK" : "null")}");
            return bmp;
        }

        public void InvalidatePreview(CatalogItem item)
        {
            if (item?.FilePath == null) return;
            _cache.TryRemove(item.FilePath, out _);
            Debug.WriteLine($"{LOG_PREFIX} Invalidate {item.FilePath}");
        }

        public void ClearAllCache()
        {
            _cache.Clear();
            Debug.WriteLine($"{LOG_PREFIX} Cleared all cache.");
        }

        private static bool IsBlockType(CatalogItem item)
        {
            return string.Equals(item.EntityType, "Block", StringComparison.OrdinalIgnoreCase);
        }
    }
}
