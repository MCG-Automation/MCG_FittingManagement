using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Hợp đồng cho thư viện Master — nguồn template gốc tại <see cref="MasterCatalogPath"/>.
    /// Mọi thao tác đọc/ghi master catalog phải đi qua interface này.
    /// </summary>
    public interface IMasterLibraryService
    {
        /// <summary>Đường dẫn file MasterCatalog.json.</summary>
        string MasterCatalogPath { get; }

        /// <summary>Đường dẫn folder chứa toàn bộ block .dwg + MasterCatalog.json.</summary>
        string MasterLibraryFolder { get; }

        /// <summary>Đọc toàn bộ item trong Master Catalog.</summary>
        List<CatalogItem> GetMasterCatalogItems();

        /// <summary>Merge danh sách item vào MasterCatalog.json (theo BlockName). Trả về (mới, cập nhật).</summary>
        Tuple<int, int> MergeIntoMaster(List<CatalogItem> items);

        /// <summary>Xoá toàn bộ item theo BlockName khỏi Master Catalog. Trả về số dòng bị xoá.</summary>
        int RemoveFromMaster(IEnumerable<string> blockNames);

        /// <summary>Wblock từng block trong AutoCAD ra file .dwg và ghi metadata vào MasterCatalog.json.</summary>
        Tuple<int, int> PublishToCentralLibrary(List<Tuple<ObjectId, CatalogItem>> itemsToPublish);

        /// <summary>
        /// Push Update — Wblock định nghĩa block hiện tại của drawing đang mở GHI ĐÈ file .dwg
        /// trong Master Library theo từng item user chọn.
        /// Chỉ ghi đè file .dwg, không sửa metadata trong MasterCatalog.json.
        /// </summary>
        PushUpdateResult PushBlocksFromCurrentDrawing(IList<CatalogItem> items);

        /// <summary>
        /// Sync catalog properties (PartNumber, Title, Description, Mass, UoM, BomType)
        /// xuống cả AttributeDefinitions trong BTR và AttributeReferences trên mọi
        /// INSERT instance trong drawing đang mở.
        /// </summary>
        PushUpdateResult SyncPropertiesFromCatalogToDrawing(IList<CatalogItem> items);
    }
}
