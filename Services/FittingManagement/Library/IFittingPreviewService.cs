using System.Windows.Media.Imaging;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Cung cấp BitmapSource preview cho từng <see cref="CatalogItem"/>.
    /// In-memory cache key theo FilePath + LastWriteTime → tự invalidate khi file .dwg đổi.
    /// </summary>
    public interface IFittingPreviewService
    {
        /// <summary>Lấy preview cho item. Trả về null nếu không có thumbnail hoặc item không phải Block.</summary>
        BitmapSource GetPreview(CatalogItem item);

        /// <summary>Xoá cache 1 item (gọi sau Push Update / Publish để force regen lần kế).</summary>
        void InvalidatePreview(CatalogItem item);

        /// <summary>Xoá toàn bộ cache (window đóng / refresh master).</summary>
        void ClearAllCache();
    }
}
