using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using MCGCadPlugin.Models.FittingManagement;

namespace MCGCadPlugin.Services.FittingManagement
{
    public interface IFittingManagementService
    {
        // --- Giai đoạn 1+2 (đã gộp): IDW → Extract DWG/JSON → Split View → Create Blocks ---
        // Phase 1 (Inventor COM) chạy trên worker thread để UI AutoCAD không bị khoá;
        // Phase 2 (AutoCAD db) tự quay về thread gốc của caller (UI thread) sau await.
        // pullFromVault=true → gọi Vault AddIn của Inventor để GetLatest mỗi file trước khi extract.
        Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, bool pullFromVault = false, IProgress<string> progress = null);

        // --- Giai đoạn 3.1: Library & Virtual Items ---
        List<CatalogItem> GetMasterCatalogItems();
        Tuple<int, int> AddItemsToProjectCatalog(string projectJsonPath, List<CatalogItem> itemsToAdd);
        Tuple<int, int> PublishToCentralLibrary(List<Tuple<ObjectId, CatalogItem>> itemsToPublish);
        void InsertBlockFromLibrary(string dwgPath, string blockName);
        CatalogItem PickGeometricFeatureFromCad();

        // --- Giai đoạn 3.2: BOM Harvester & Ballooning ---
        List<BomHarvestRecord> HarvestStructureBom();
        List<BomHarvestRecord> HarvestInterfaceBom();
        void MassAutoBalloon();
        void InteractivePlaceBalloon();

        // --- Giai đoạn 3.3: Block Utilities ---
        void InteractiveBlockRenameClone();
        void SmartReplaceBlocks();
        void RedefineBlocksFromLibrary();
        void ExtractEntitiesFromBlock();
        void ChangeBlockBasePoint();
        void AddEntitiesToBlock();

        // --- Giai đoạn 3.4: Drawing Collection ---
        // Gom Model Space của nhiều file .dwg vào bản vẽ hiện hành:
        // - Đổi tên block theo [TênFile]_[TênBlock] (giữ nguyên A1, CAS_HEAD).
        // - Purge rác trước khi clone.
        // - Xếp ngang từ trái sang phải, khoảng hở 1000.
        // - Phase tiền xử lý (side db) chạy trên worker thread; phase clone vào current doc quay về UI thread sau await.
        Task<ImportResult> CollectDrawingsAsync(string[] dwgPaths, IProgress<string> progress = null);
    }
}