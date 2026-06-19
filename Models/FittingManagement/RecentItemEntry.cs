namespace MCG_FittingManagement.Models.FittingManagement
{
    /// <summary>
    /// Một entry trong danh sách "Recently" của Library window.
    /// Lưu BlockName làm key để lookup ngược catalog hiện tại,
    /// kèm timestamp UTC để sort và giới hạn FIFO.
    /// </summary>
    public class RecentItemEntry
    {
        /// <summary>Khoá lookup — phải khớp <see cref="CatalogItem.BlockName"/>.</summary>
        public string BlockName { get; set; }

        /// <summary>Thời điểm đẩy entry, dùng cho sort và FIFO. UTC ticks.</summary>
        public long TimestampUtc { get; set; }
    }
}
