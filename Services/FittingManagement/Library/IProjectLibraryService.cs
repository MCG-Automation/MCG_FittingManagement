using System;
using System.Collections.Generic;
using MCGCadPlugin.Models.FittingManagement;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Hợp đồng cho thư viện Project — file JSON do user tự đặt vị trí, gắn với 1 project cụ thể.
    /// Mọi thao tác đọc/ghi project catalog phải đi qua interface này.
    /// </summary>
    public interface IProjectLibraryService
    {
        /// <summary>Đọc danh sách item từ file project JSON. Trả về list rỗng nếu file không tồn tại hoặc parse fail.</summary>
        List<ProjectCatalogItem> LoadProjectCatalog(string projectJsonPath);

        /// <summary>Tạo file project JSON rỗng tại đường dẫn chỉ định (overwrite nếu đã tồn tại).</summary>
        void CreateProjectCatalog(string projectJsonPath);

        /// <summary>Merge danh sách item vào project catalog (theo BlockName). Trả về (mới, cập nhật).</summary>
        Tuple<int, int> MergeIntoProject(string projectJsonPath, List<CatalogItem> items);

        /// <summary>Ghi đè toàn bộ project catalog xuống file JSON.</summary>
        void SaveProjectCatalog(string projectJsonPath, List<ProjectCatalogItem> catalog);

        /// <summary>Xoá item theo BlockName khỏi project catalog. Trả về số dòng bị xoá.</summary>
        int RemoveFromProject(string projectJsonPath, IEnumerable<string> blockNames);

        /// <summary>
        /// Tự gán <c>ProjectPosNum</c> dạng "001", "002"... cho fitting Detail/Hull, group theo PartNumber.
        /// Lưu trực tiếp xuống file project JSON. Trả về số group đã gán pos.
        /// </summary>
        int AutoAssignPositions(string projectJsonPath);
    }
}
