using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Newtonsoft.Json;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Phần partial xử lý Master Library — đọc MasterCatalog.json, publish block, insert block.
    /// </summary>
    public partial class FittingManagementService : IFittingManagementService, IMasterLibraryService
    {
        private const string LOG_PREFIX = "[FittingManagementService]";
        private readonly string _libraryFolderPath = @"C:\Temp_BIM_Library";

        public string MasterLibraryFolder => _libraryFolderPath;
        public string MasterCatalogPath => Path.Combine(_libraryFolderPath, "MasterCatalog.json");

        public FittingManagementService()
        {
            Debug.WriteLine($"{LOG_PREFIX} Khởi tạo service.");
        }

        public List<CatalogItem> GetMasterCatalogItems()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu lấy dữ liệu Master Catalog...");
            try
            {
                if (!File.Exists(MasterCatalogPath))
                {
                    Debug.WriteLine($"{LOG_PREFIX} CẢNH BÁO: Không tìm thấy file MasterCatalog.json.");
                    return new List<CatalogItem>();
                }

                string json = File.ReadAllText(MasterCatalogPath);
                var items = JsonConvert.DeserializeObject<List<CatalogItem>>(json) ?? new List<CatalogItem>();
                Debug.WriteLine($"{LOG_PREFIX} Đọc THÀNH CÔNG {items.Count} items từ Master Catalog.");
                return items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI GetMasterCatalogItems: {ex.Message}");
                throw;
            }
        }

        public Tuple<int, int> MergeIntoMaster(List<CatalogItem> items)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu MergeIntoMaster ({items?.Count ?? 0} items)...");
            try
            {
                if (!Directory.Exists(_libraryFolderPath)) Directory.CreateDirectory(_libraryFolderPath);
                var result = CatalogJsonStore.MergeItems(MasterCatalogPath, items);
                Debug.WriteLine($"{LOG_PREFIX} MergeIntoMaster THÀNH CÔNG (Mới: {result.Item1}, Sửa: {result.Item2}).");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI MergeIntoMaster: {ex.Message}");
                throw;
            }
        }

        public int RemoveFromMaster(IEnumerable<string> blockNames)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu RemoveFromMaster...");
            try
            {
                int removed = CatalogJsonStore.RemoveItems(MasterCatalogPath, blockNames);
                Debug.WriteLine($"{LOG_PREFIX} RemoveFromMaster THÀNH CÔNG ({removed} items).");
                return removed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI RemoveFromMaster: {ex.Message}");
                throw;
            }
        }

        public Tuple<int, int> PublishToCentralLibrary(List<Tuple<ObjectId, CatalogItem>> itemsToPublish)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu Publish lên Central Library...");
            try
            {
                if (!Directory.Exists(_libraryFolderPath)) Directory.CreateDirectory(_libraryFolderPath);

                Database db = HostApplicationServices.WorkingDatabase;
                List<CatalogItem> successItems = new List<CatalogItem>();

                foreach (var item in itemsToPublish)
                {
                    ObjectId blockId = item.Item1;
                    CatalogItem info = item.Item2;

                    try
                    {
                        using (Database exportDb = db.Wblock(blockId))
                        {
                            exportDb.Insunits = UnitsValue.Millimeters;

                            // B-2: copy block preview icon -> thumbnail của file mới
                            // → file .dwg sau Save sẽ có sẵn preview, không cần render runtime.
                            var icon = DwgThumbnailExtractor.GetBlockPreviewIcon(db, blockId);
                            if (icon != null) exportDb.ThumbnailBitmap = icon;

                            if (File.Exists(info.FilePath)) File.Delete(info.FilePath);
                            exportDb.SaveAs(info.FilePath, DwgVersion.Current);
                        }
                        successItems.Add(info);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} LỖI khi export {info.BlockName}: {ex.Message}");
                    }
                }

                if (successItems.Count > 0)
                {
                    var result = CatalogJsonStore.MergeItems(MasterCatalogPath, successItems);
                    Debug.WriteLine($"{LOG_PREFIX} Publish THÀNH CÔNG {successItems.Count} items.");
                    return result;
                }

                return new Tuple<int, int>(0, 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI PublishToCentralLibrary: {ex.Message}");
                throw;
            }
        }

        public PushUpdateResult PushBlocksFromCurrentDrawing(IList<CatalogItem> items)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu PushBlocksFromCurrentDrawing ({items?.Count ?? 0} items)...");
            var result = new PushUpdateResult();
            if (items == null || items.Count == 0) return result;

            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) throw new InvalidOperationException("Không có drawing nào đang mở.");
                Database db = doc.Database;

                using (DocumentLock loc = doc.LockDocument())
                {
                    // 1. Resolve BlockName -> BTR ObjectId trong drawing đang mở
                    var lookup = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        foreach (var item in items)
                        {
                            if (string.IsNullOrEmpty(item?.BlockName)) continue;
                            if (bt.Has(item.BlockName) && !lookup.ContainsKey(item.BlockName))
                                lookup[item.BlockName] = bt[item.BlockName];
                        }
                        tr.Commit();
                    }

                    // 2. Wblock từng cái ra file .dwg trong Library (Wblock tự handle transaction nội bộ)
                    foreach (var item in items)
                    {
                        string blockName = item?.BlockName ?? "<empty>";
                        try
                        {
                            if (string.IsNullOrEmpty(item?.BlockName) || string.IsNullOrEmpty(item?.FilePath))
                            {
                                result.NotFoundInDrawing.Add(blockName);
                                continue;
                            }

                            if (!lookup.TryGetValue(item.BlockName, out ObjectId btrId))
                            {
                                result.NotFoundInDrawing.Add(item.BlockName);
                                Debug.WriteLine($"{LOG_PREFIX} Skip (không có trong drawing): {item.BlockName}");
                                continue;
                            }

                            string folder = Path.GetDirectoryName(item.FilePath);
                            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);

                            using (Database exportDb = db.Wblock(btrId))
                            {
                                exportDb.Insunits = UnitsValue.Millimeters;

                                // B-2: copy block preview icon -> thumbnail file
                                var icon = DwgThumbnailExtractor.GetBlockPreviewIcon(db, btrId);
                                if (icon != null) exportDb.ThumbnailBitmap = icon;

                                if (File.Exists(item.FilePath)) File.Delete(item.FilePath);
                                exportDb.SaveAs(item.FilePath, DwgVersion.Current);
                            }
                            result.Updated.Add(item.BlockName);
                            Debug.WriteLine($"{LOG_PREFIX} Pushed: {item.BlockName} -> {item.FilePath}");
                        }
                        catch (Exception ex)
                        {
                            result.AddError(blockName, ex.Message);
                            Debug.WriteLine($"{LOG_PREFIX} LỖI push {blockName}: {ex.Message}");
                        }
                    }
                }

                Debug.WriteLine($"{LOG_PREFIX} PushBlocksFromCurrentDrawing XONG. OK={result.SuccessCount}, Skipped={result.SkippedCount}, Failed={result.FailCount}.");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI PushBlocksFromCurrentDrawing: {ex.Message}");
                throw;
            }
        }

        public void InsertBlockFromLibrary(string dwgPath, string blockName)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu lệnh InsertBlockFromLibrary: {blockName}...");
            try
            {
                // Tier 1 fallback: Virtual Item không có file .dwg → tìm block trong drawing đang mở
                if (string.IsNullOrEmpty(dwgPath))
                {
                    InsertVirtualBlockFromCurrentDrawing(blockName);
                    return;
                }

                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor ed = doc.Editor;

                if (!File.Exists(dwgPath)) throw new FileNotFoundException($"Không tìm thấy file: {dwgPath}");

                using (DocumentLock loc = doc.LockDocument())
                {
                    ObjectId btrId = ObjectId.Null;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            if (!bt.Has(blockName))
                            {
                                using (Database sideDb = new Database(false, true))
                                {
                                    sideDb.ReadDwgFile(dwgPath, FileShare.Read, true, "");
                                    sideDb.Insunits = db.Insunits;
                                    btrId = db.Insert(blockName, sideDb, true);
                                }
                            }
                            else
                            {
                                btrId = bt[blockName];
                            }

                            BlockTableRecord targetBtr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                            targetBtr.Units = db.Insunits;

                            bool hasPosNum = false;
                            foreach (ObjectId childId in targetBtr)
                            {
                                DBObject obj = tr.GetObject(childId, OpenMode.ForRead);
                                if (obj is AttributeDefinition attDef && attDef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasPosNum = true;
                                    break;
                                }
                            }

                            if (!hasPosNum) FittingBlockUtility.AddAttributeDef(targetBtr, tr, "POS_NUM", "", "Position Number", true);

                            tr.Commit();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (Nạp định nghĩa Block): {ex.Message}");
                            throw;
                        }
                    }

                    PromptPointOptions ppo = new PromptPointOptions($"\nSelect insertion point for '{blockName}' (or press ESC to skip): ");
                    PromptPointResult ppr = ed.GetPoint(ppo);

                    if (ppr.Status == PromptStatus.OK)
                    {
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            try
                            {
                                FittingBlockUtility.InsertBlockReference(db, tr, btrId, ppr.Value);
                                tr.Commit();
                                Debug.WriteLine($"{LOG_PREFIX} InsertBlockFromLibrary THÀNH CÔNG.");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (Chèn Block Reference): {ex.Message}");
                                throw;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI InsertBlockFromLibrary: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tier 1 fallback: chèn block trực tiếp từ BlockTable của drawing đang mở,
        /// dùng khi Virtual Item chưa có file .dwg (FilePath rỗng).
        /// </summary>
        private void InsertVirtualBlockFromCurrentDrawing(string blockName)
        {
            Debug.WriteLine($"{LOG_PREFIX} InsertVirtualBlockFromCurrentDrawing: {blockName}...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            using (DocumentLock loc = doc.LockDocument())
            {
                ObjectId btrId = ObjectId.Null;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        if (!bt.Has(blockName))
                            throw new Exception(
                                $"Block '{blockName}' không có trong drawing hiện tại.\n" +
                                $"Hãy mở drawing gốc chứa block này rồi thử lại.");

                        btrId = bt[blockName];

                        // Đảm bảo POS_NUM attribute tồn tại (đồng nhất với InsertBlockFromLibrary)
                        BlockTableRecord targetBtr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                        bool hasPosNum = false;
                        foreach (ObjectId childId in targetBtr)
                        {
                            DBObject obj = tr.GetObject(childId, OpenMode.ForRead);
                            if (obj is AttributeDefinition attDef &&
                                attDef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                            {
                                hasPosNum = true;
                                break;
                            }
                        }
                        if (!hasPosNum)
                            FittingBlockUtility.AddAttributeDef(targetBtr, tr, "POS_NUM", "", "Position Number", true);

                        tr.Commit();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (InsertVirtual lookup): {ex.Message}");
                        throw;
                    }
                }

                PromptPointOptions ppo = new PromptPointOptions(
                    $"\nSelect insertion point for '{blockName}' (or press ESC to skip): ");
                PromptPointResult ppr = ed.GetPoint(ppo);

                if (ppr.Status == PromptStatus.OK)
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            FittingBlockUtility.InsertBlockReference(db, tr, btrId, ppr.Value);
                            tr.Commit();
                            Debug.WriteLine($"{LOG_PREFIX} InsertVirtualBlockFromCurrentDrawing THÀNH CÔNG.");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (InsertVirtual ref): {ex.Message}");
                            throw;
                        }
                    }
                }
            }
        }
    }
}
