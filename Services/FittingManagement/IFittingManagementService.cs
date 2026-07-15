using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Hợp đồng chính cho FittingManagement: import IDW, BOM harvest, balloon, block utilities,
    /// drawing collection và 2 thao tác library "thuộc tính chéo" (Insert + Pick virtual item).
    /// Library CRUD (catalog của Project Folder đang active) đã tách ra <see cref="IMasterLibraryService"/>.
    /// </summary>
    public interface IFittingManagementService
    {
        // --- Giai đoạn 1+2: IDW → Extract DWG/JSON → Split View → Create Blocks ---
        Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, bool pullFromVault = false, IProgress<string> progress = null);

        // --- Giai đoạn 3.1: Library — operations chéo (Insert vào CAD, Pick từ CAD) ---
        // Đọc/ghi catalog đã chuyển sang IMasterLibraryService.
        void InsertBlockFromLibrary(string dwgPath, string blockName);

        /// <summary>
        /// Chèn nhiều block cùng lúc: load tất cả definitions, hỏi 1 điểm duy nhất,
        /// trải các block theo trục X với khoảng cách tự động theo extents từng block.
        /// </summary>
        void InsertMultipleBlocksFromLibrary(IList<CatalogItem> items);

        CatalogItem PickGeometricFeatureFromCad();

        /// <summary>
        /// Chèn "Fitting Table" — bảng lưới N hàng (mỗi hàng 1 fitting, gom theo PartNumber từ
        /// <paramref name="projectItems"/>) x cột (Views/Pos./Vault Name/Part ID/X.Class/Description/
        /// Weight/Designer). Cột "Views" gộp tất cả hình chiếu của fitting đó vào chung 1 ô, chèn ở tỉ
        /// lệ THẬT 1:1. <paramref name="tableTitle"/> hiện trên dòng Title phía trên bảng (thường là
        /// tên Category user đang chọn khi bấm Insert — xem FittingTableWindow); null/rỗng thì chỉ hiện
        /// "FITTING TABLE" trơn.
        /// Hỗ trợ UPDATE-IN-PLACE (CHỈ 1 CLICK): trước khi vẽ, hỏi user click chọn (tùy chọn) Title/1
        /// entity của Fitting Table cũ trong bản vẽ — nếu chọn đúng, xóa bảng cũ rồi vẽ bảng mới NGAY
        /// TẠI VỊ TRÍ CŨ (không hỏi lại điểm chèn), giữ nguyên ngày Created gốc, và tô ĐỎ những hàng có
        /// dữ liệu thay đổi so với lần trước; nếu bỏ qua (Enter), chèn bảng mới như bình thường (hỏi
        /// điểm chèn).
        /// Trả về đường dẫn file báo cáo chẩn đoán (.txt) để đánh giá chất lượng bảng vừa chèn mà
        /// không cần gửi ảnh chụp màn hình; trả về null nếu user hủy chọn điểm chèn.
        /// </summary>
        string InsertFittingTable(IList<CatalogItem> projectItems, string tableTitle = null);

        // --- Giai đoạn 3.2: BOM Harvester & Ballooning ---
        List<BomHarvestRecord> HarvestStructureBom();
        List<BomHarvestRecord> HarvestInterfaceBom();
        void MassAutoBalloon();
        void InteractivePlaceBalloon();

        // --- Giai đoạn 3.3: Block Utilities ---
        void InteractiveBlockRenameClone();
        void SmartReplaceBlocks();
        void RedefineBlocksFromOpenDrawing();
        void ExtractEntitiesFromBlock();
        void ChangeBlockBasePoint();
        void AddEntitiesToBlock();

        // --- Giai đoạn 3.4: Drawing Collection ---
        Task<ImportResult> CollectDrawingsAsync(string[] dwgPaths, IProgress<string> progress = null);
        Task<ImportResult> CollectIdwDrawingsAsync(string[] idwPaths, IProgress<string> progress = null);
    }
}
