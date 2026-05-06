using System.Collections.Generic;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Theo dõi các item vừa được dùng trong 1 Library window (Master hoặc Project).
    /// Mỗi window khởi tạo 1 instance riêng với store file riêng.
    /// </summary>
    public interface IRecentItemsTracker
    {
        /// <summary>Đẩy <paramref name="blockName"/> lên đầu list (move-to-top), giới hạn FIFO 15 entries.</summary>
        void Track(string blockName);

        /// <summary>Trả về danh sách BlockName, mới nhất lên đầu. Đọc trực tiếp từ store.</summary>
        List<string> GetRecentBlockNames();
    }
}
