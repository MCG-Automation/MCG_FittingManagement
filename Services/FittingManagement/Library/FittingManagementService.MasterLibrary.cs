using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
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

        public PushUpdateResult SyncPropertiesFromCatalogToDrawing(IList<CatalogItem> items)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu SyncPropertiesFromCatalogToDrawing ({items?.Count ?? 0} items)...");
            var result = new PushUpdateResult();
            if (items == null || items.Count == 0) return result;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("Không có drawing nào đang mở.");
            Database db = doc.Database;

            using (DocumentLock loc = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(item?.BlockName)) continue;

                        // BlockName có thể chứa nhiều view, ngăn cách bằng ";"
                        var blockNames = item.BlockName
                            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));

                        foreach (var bName in blockNames)
                        {
                            if (!bt.Has(bName))
                            {
                                result.NotFoundInDrawing.Add(bName);
                                Debug.WriteLine($"{LOG_PREFIX} SyncToDrawing skip (không có trong drawing): {bName}");
                                continue;
                            }

                            try
                            {
                                // 1. Cập nhật AttributeDefinitions trong BTR
                                ObjectId btrId = bt[bName];
                                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                                FittingBlockUtility.EmbedCatalogProperties(btr, tr, item);

                                // 2. Propagate xuống tất cả INSERT instances trong drawing
                                int refCount = FittingBlockUtility.SyncAttributeReferences(db, tr, bName, item);

                                result.Updated.Add(bName);
                                Debug.WriteLine($"{LOG_PREFIX} SyncToDrawing OK: {bName} ({refCount} instances updated)");
                            }
                            catch (Exception ex)
                            {
                                result.AddError(bName, ex.Message);
                                Debug.WriteLine($"{LOG_PREFIX} LỖI SyncToDrawing {bName}: {ex.Message}");
                            }
                        }
                    }

                    tr.Commit();
                    Debug.WriteLine($"{LOG_PREFIX} SyncPropertiesFromCatalogToDrawing XONG. OK={result.SuccessCount}, Failed={result.FailCount}.");
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT SyncPropertiesFromCatalogToDrawing: {ex.Message}");
                    throw;
                }
            }
        }

        public void InsertMultipleBlocksFromLibrary(IList<CatalogItem> items)
        {
            Debug.WriteLine($"{LOG_PREFIX} InsertMultipleBlocksFromLibrary ({items?.Count ?? 0} items)...");
            if (items == null || items.Count == 0) return;

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("Không có drawing nào đang mở.");
            Database db = doc.Database;
            Editor   ed = doc.Editor;

            using (DocumentLock loc = doc.LockDocument())
            {
                // Phase 1: Load tất cả definitions + tính width
                var plan = new List<(ObjectId btrId, double width)>();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                        foreach (var item in items)
                        {
                            if (string.IsNullOrEmpty(item?.BlockName)) continue;

                            ObjectId btrId = ObjectId.Null;

                            if (string.IsNullOrEmpty(item.FilePath))
                            {
                                // Tier 1 fallback: tìm trong drawing hiện tại
                                if (!bt.Has(item.BlockName))
                                {
                                    Debug.WriteLine($"{LOG_PREFIX} Multi-insert skip (không có trong drawing): {item.BlockName}");
                                    continue;
                                }
                                btrId = bt[item.BlockName];
                            }
                            else
                            {
                                if (!File.Exists(item.FilePath))
                                {
                                    Debug.WriteLine($"{LOG_PREFIX} Multi-insert skip (file không tồn tại): {item.FilePath}");
                                    continue;
                                }
                                if (!bt.Has(item.BlockName))
                                {
                                    using (Database sideDb = new Database(false, true))
                                    {
                                        sideDb.ReadDwgFile(item.FilePath, FileShare.Read, true, "");
                                        sideDb.Insunits = db.Insunits;
                                        btrId = db.Insert(item.BlockName, sideDb, true);
                                    }
                                }
                                else
                                {
                                    btrId = bt[item.BlockName];
                                }
                            }

                            if (btrId.IsNull) continue;

                            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                            btr.Units = db.Insunits;

                            bool hasPosNum = false;
                            foreach (ObjectId childId in btr)
                            {
                                if (tr.GetObject(childId, OpenMode.ForRead) is AttributeDefinition ad &&
                                    ad.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                                { hasPosNum = true; break; }
                            }
                            if (!hasPosNum)
                                FittingBlockUtility.AddAttributeDef(btr, tr, "POS_NUM", "", "Position Number", true);

                            double width = ComputeBlockWidth(tr, btr);
                            plan.Add((btrId, width));
                            Debug.WriteLine($"{LOG_PREFIX} Loaded '{item.BlockName}', width={width:F1}");
                        }

                        tr.Commit();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (Multi-insert Phase1): {ex.Message}");
                        throw;
                    }
                }

                if (plan.Count == 0) return;

                // Phase 2: 1 lần hỏi điểm chèn
                double defaultWidth = plan.Max(p => p.width > 0 ? p.width : 0);
                if (defaultWidth <= 0) defaultWidth = 200.0;
                const double GAP_RATIO = 0.3;
                double gap = defaultWidth * GAP_RATIO;

                PromptPointOptions ppo = new PromptPointOptions(
                    $"\nSelect base insertion point for {plan.Count} block(s) (or press ESC to cancel): ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK)
                {
                    Debug.WriteLine($"{LOG_PREFIX} Multi-insert bị hủy.");
                    return;
                }

                // Phase 3: Chèn tất cả, trải theo trục X
                double currentX = ppr.Value.X;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        foreach (var (btrId, width) in plan)
                        {
                            double blockW = width > 0 ? width : defaultWidth;
                            Point3d pt = new Point3d(currentX, ppr.Value.Y, ppr.Value.Z);
                            FittingBlockUtility.InsertBlockReference(db, tr, btrId, pt);
                            currentX += blockW + gap;
                        }
                        tr.Commit();
                        Debug.WriteLine($"{LOG_PREFIX} InsertMultipleBlocksFromLibrary THÀNH CÔNG ({plan.Count} blocks).");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (Multi-insert Phase3): {ex.Message}");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Tính chiều rộng (trục X) của block dựa trên GeometricExtents các entity con.
        /// Trả về 0 nếu không tính được (block rỗng hoặc toàn bộ entity ném exception).
        /// </summary>
        private static double ComputeBlockWidth(Transaction tr, BlockTableRecord btr)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;

            foreach (ObjectId id in btr)
            {
                try
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ent is AttributeDefinition) continue;
                    Extents3d ext = ent.GeometricExtents;
                    if (ext.MinPoint.X < minX) minX = ext.MinPoint.X;
                    if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                }
                catch { }
            }

            return (minX < double.MaxValue && maxX > double.MinValue) ? maxX - minX : 0.0;
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
