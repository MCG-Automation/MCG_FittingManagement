using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Hợp đồng chính cho FittingManagement: import IDW, BOM harvest, balloon, block utilities,
    /// drawing collection và 2 thao tác library "thuộc tính chéo" (Insert + Pick virtual item).
    /// Library CRUD đã tách ra <see cref="IMasterLibraryService"/> và <see cref="IProjectLibraryService"/>.
    /// </summary>
    public interface IFittingManagementService
    {
        // --- Giai đoạn 1+2: IDW → Extract DWG/JSON → Split View → Create Blocks ---
        Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, bool pullFromVault = false, IProgress<string> progress = null);

        // --- Giai đoạn 3.1: Library — operations chéo (Insert vào CAD, Pick từ CAD) ---
        // Đọc/ghi catalog đã chuyển sang IMasterLibraryService / IProjectLibraryService.
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
        void RedefineBlocksFromOpenDrawing();
        void ExtractEntitiesFromBlock();
        void ChangeBlockBasePoint();
        void AddEntitiesToBlock();

        // --- Giai đoạn 3.4: Drawing Collection ---
        Task<ImportResult> CollectDrawingsAsync(string[] dwgPaths, IProgress<string> progress = null);
        Task<ImportResult> CollectIdwDrawingsAsync(string[] idwPaths, IProgress<string> progress = null);
    }
}
