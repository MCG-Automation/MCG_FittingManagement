using System.Collections.Generic;

namespace MCGCadPlugin.Models.FittingManagement
{
    /// <summary>
    /// Kết quả của thao tác Push Update — Wblock block từ drawing đang mở
    /// về file .dwg trong Master Library.
    /// </summary>
    public class PushUpdateResult
    {
        /// <summary>BlockName đã update thành công.</summary>
        public List<string> Updated { get; set; } = new List<string>();

        /// <summary>BlockName user chọn nhưng không có trong BlockTable của drawing đang mở.</summary>
        public List<string> NotFoundInDrawing { get; set; } = new List<string>();

        /// <summary>Lỗi chi tiết khi Wblock/SaveAs thất bại (format: "blockName: error").</summary>
        public List<string> Errors { get; set; } = new List<string>();

        public int SuccessCount => Updated.Count;
        public int SkippedCount => NotFoundInDrawing.Count;
        public int FailCount => Errors.Count;

        public void AddError(string blockName, string message) => Errors.Add($"• {blockName}: {message}");
    }
}
