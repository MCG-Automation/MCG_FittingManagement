using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Hợp đồng cho catalog của Project Folder đang active (<see cref="ActiveProjectContext"/>) —
    /// nguồn dữ liệu tại <see cref="MasterCatalogPath"/> (file <c>FittingCatalog.json</c> bên trong
    /// Project Folder). Mỗi project 1 catalog riêng, độc lập — không còn Master Library toàn công ty.
    /// Mọi thao tác đọc/ghi catalog phải đi qua interface này.
    /// </summary>
    public interface IMasterLibraryService
    {
        /// <summary>Đường dẫn file FittingCatalog.json của Project Folder đang active.</summary>
        string MasterCatalogPath { get; }

        /// <summary>Đường dẫn Project Folder đang active — chứa toàn bộ block .dwg + FittingCatalog.json.</summary>
        string MasterLibraryFolder { get; }

        /// <summary>Đọc toàn bộ item trong catalog của Project Folder đang active. Trả về rỗng nếu chưa có project nào active.</summary>
        List<CatalogItem> GetMasterCatalogItems();

        /// <summary>Merge danh sách item vào FittingCatalog.json (theo BlockName). Trả về (mới, cập nhật).</summary>
        Tuple<int, int> MergeIntoMaster(List<CatalogItem> items);

        /// <summary>Xoá toàn bộ item theo BlockName khỏi catalog. Trả về số dòng bị xoá.</summary>
        int RemoveFromMaster(IEnumerable<string> blockNames);

        /// <summary>
        /// Gán tự động Position Number cho các fitting DETAIL/HULL trong catalog (gom theo PartNumber).
        /// Trả về số nhóm đã gán.
        /// </summary>
        int AutoAssignPositions();

        /// <summary>Wblock từng block trong AutoCAD ra file .dwg và ghi metadata vào FittingCatalog.json.</summary>
        Tuple<int, int> PublishToCentralLibrary(List<Tuple<ObjectId, CatalogItem>> itemsToPublish);

        /// <summary>
        /// Push Update — Wblock định nghĩa block hiện tại của drawing đang mở GHI ĐÈ file .dwg
        /// trong Project Folder theo từng item user chọn.
        /// Chỉ ghi đè file .dwg, không sửa metadata trong FittingCatalog.json.
        /// </summary>
        PushUpdateResult PushBlocksFromCurrentDrawing(IList<CatalogItem> items);

        /// <summary>
        /// Sync catalog properties (PartNumber, Title, Description, Mass, UoM, BomType)
        /// xuống cả AttributeDefinitions trong BTR và AttributeReferences trên mọi
        /// INSERT instance trong drawing đang mở.
        /// </summary>
        PushUpdateResult SyncPropertiesFromCatalogToDrawing(IList<CatalogItem> items);

        /// <summary>
        /// Nhúng đầy đủ 10 attribute chuẩn (PART_NUMBER, DESCRIPTION, MATERIAL, MASS, REVISION,
        /// DESIGNER, TITLE, BOM_TYPE, POS_NUM, VIEW_NAME) vào block definition(s) trong drawing đang mở —
        /// dùng cho "Add from CAD" để block tạo theo cách này có attribute tương đương block Inventor.
        /// <paramref name="item"/>.BlockName có thể chứa nhiều tên ngăn cách bằng ";" (multi-block virtual item).
        /// </summary>
        void EmbedBimAttributesInDrawing(CatalogItem item);
    }
}
