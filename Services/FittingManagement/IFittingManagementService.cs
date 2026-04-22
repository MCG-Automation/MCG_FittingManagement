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
        Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, IProgress<string> progress = null);

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
    }
}