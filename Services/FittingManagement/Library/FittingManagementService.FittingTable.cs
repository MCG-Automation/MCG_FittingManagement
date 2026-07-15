using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Chèn "Fitting Table" — bảng lưới N hàng (1 hàng = 1 fitting) x cột (Views/Pos./Vault Name/
    /// Part ID/X.Class/Description/Weight/Designer) vẽ tay, có 1 dòng Title phía trên = tên Category
    /// user đang chọn lúc Insert (1 bản vẽ có thể chứa nhiều Fitting Table, mỗi bảng ứng với 1
    /// category khác nhau) + 1 dòng "Created" phía dưới đáy bảng.
    /// Views luôn chèn ở TỈ LỆ THẬT 1:1 (Block Scale luôn = 1 trong Properties palette — KHÔNG scale
    /// block để "vừa khít" ô như thiết kế cũ) — mỗi hàng cao thấp khác nhau tuỳ kích thước thật của
    /// fitting đó (rowHeight biến thiên, không cố định). Cỡ chữ ĐỒNG NHẤT cho cả bảng (1 textHeight
    /// chung, tính theo trung vị "median" kích thước view thật — "textBasis"). Độ rộng cột tính theo
    /// ĐỘ DÀI THẬT của dữ liệu từng cột (không phải hằng số ước lượng cố định) để luôn đủ chỗ chứa hết
    /// chữ. Vị trí từng view canh chính xác theo local extents của block (không giả định origin nằm ở
    /// góc nào) — tránh chồng lấn lên đường lưới.
    /// Layer riêng "Mechanical-FittingTable" cho text + lưới (không dùng chung layer
    /// "Mechanical-AM_9" — layer đó dành riêng cho nhãn tên block, xem FittingBlockUtility.AddNameLabelText).
    /// Hỗ trợ UPDATE-IN-PLACE CHỈ 1 CLICK: mỗi entity của 1 lần vẽ (kể cả Title + dòng Created) được
    /// gắn metadata (XData, RegApp "MCG_FITTING_TABLE") gồm Table ID + ngày Created GỐC + điểm chèn
    /// GỐC — user click chọn Title (hoặc bất kỳ entity nào) của bảng cũ để tool tự xóa đúng bảng đó
    /// VÀ vẽ lại NGAY TẠI VỊ TRÍ CŨ (không hỏi lại điểm chèn), giữ nguyên ngày Created, đồng thời tô
    /// ĐỎ những hàng có dữ liệu thay đổi so với snapshot lần trước (lưu qua Xrecord trong Extension
    /// Dictionary của header background).
    /// </summary>
    public partial class FittingManagementService
    {
        private const string FITTING_TABLE_LAYER = "Mechanical-FittingTable";
        private const short FITTING_TABLE_LAYER_COLOR = 3; // Green — phân biệt với hidden(6)/center(4)/label(7)

        // RegApp dùng để gắn XData (Table ID + Created + điểm chèn GỐC) lên MỌI entity của 1 Fitting
        // Table. Mục đích: khi user muốn UPDATE 1 bảng đã có sẵn (thêm/sửa fitting), tool cần xóa đúng
        // bảng cũ + biết vẽ lại ở đâu — nhận diện qua entity user CLICK CHỌN THỦ CÔNG (không tự động
        // quét/đoán cả bản vẽ — tránh xóa nhầm bảng khác nếu có nhiều Fitting Table).
        private const string FITTING_TABLE_XDATA_APP = "MCG_FITTING_TABLE";

        // Key Xrecord (Extension Dictionary của header background) lưu snapshot dữ liệu từng hàng —
        // dùng để so sánh phát hiện hàng nào thay đổi khi Update (tô đỏ).
        private const string ROW_SNAPSHOT_DICT_KEY = "MCG_FITTING_TABLE_ROWS";

        /// <summary>
        /// Chèn bảng lưới TẤT CẢ fitting trong <paramref name="projectItems"/> (gom theo PartNumber,
        /// mỗi group = 1 hàng). <paramref name="tableTitle"/> hiện trên dòng Title phía trên bảng.
        /// Trước khi vẽ, hỏi user chọn (tùy chọn) Title/1 entity của bảng CŨ để UPDATE — nếu chọn
        /// đúng, xóa toàn bộ bảng cũ đó rồi vẽ bảng mới NGAY TẠI VỊ TRÍ CŨ (không hỏi lại điểm chèn),
        /// giữ nguyên ngày Created gốc, tô đỏ hàng thay đổi; nếu Enter/bỏ qua, chèn bảng MỚI như bình
        /// thường (hỏi điểm chèn). Trả về đường dẫn file báo cáo chẩn đoán (.txt) — liệt kê kích thước
        /// từng thành phần + kiểm tra overlap của từng view với ranh giới ô/lưới, dùng để đánh giá
        /// chất lượng bảng vừa chèn mà không cần gửi ảnh chụp màn hình. Trả về null nếu user hủy chọn điểm chèn.
        /// </summary>
        public string InsertFittingTable(IList<CatalogItem> projectItems, string tableTitle = null)
        {
            var blockItems = projectItems?
                .Where(i => i != null && i.EntityType == "Block" && !string.IsNullOrEmpty(i.BlockName) && !string.IsNullOrEmpty(i.PartNumber))
                .ToList();
            if (blockItems == null || blockItems.Count == 0)
                throw new InvalidOperationException("Không có fitting (Block-type) nào trong Active Project để tạo Fitting Table.");

            // Gom theo PartNumber -> mỗi group = 1 hàng; sort view trong group theo BlockName;
            // sort hàng theo ProjectPosNum (số) rồi PartNumber.
            var rows = blockItems
                .GroupBy(i => i.PartNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(i => i.BlockName, StringComparer.OrdinalIgnoreCase).ToList())
                .OrderBy(g => ParsePosForSort(g[0].ProjectPosNum))
                .ThenBy(g => g[0].PartNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Debug.WriteLine($"{LOG_PREFIX} InsertFittingTable ({rows.Count} fitting(s), {blockItems.Count} view(s))...");

            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("Không có drawing nào đang mở.");
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock loc = doc.LockDocument())
            {
                // Phase 1: load definitions + tính local extents (width/height/minX/minY) từng view.
                // minX/minY (theo hệ toạ độ CỦA BLOCK, chưa transform) cần để canh CHÍNH XÁC vị trí
                // chèn — không giả định origin của block nằm ở góc nào của hình học.
                var viewSizes = new Dictionary<CatalogItem, (ObjectId btrId, double minX, double minY, double width, double height)>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        foreach (var row in rows)
                        {
                            foreach (var item in row)
                            {
                                ObjectId btrId = FittingBlockUtility.ResolveOrLoadBlockDefinition(db, tr, bt, item.BlockName, item.FilePath);
                                if (btrId.IsNull)
                                {
                                    Debug.WriteLine($"{LOG_PREFIX} FittingTable skip (không load được): {item.BlockName}");
                                    continue;
                                }
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                                btr.Units = db.Insunits;
                                if (!TryComputeLocalExtents(tr, btr, out double minX, out double minY, out double maxX, out double maxY))
                                    continue;
                                viewSizes[item] = (btrId, minX, minY, maxX - minX, maxY - minY);
                            }
                        }
                        tr.Commit();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (FittingTable Phase1): {ex.Message}");
                        throw;
                    }
                }

                if (viewSizes.Count == 0)
                    throw new InvalidOperationException("Không load được view nào cho Fitting Table (kiểm tra file .dwg trong Master Library).");

                // Update-in-place (CHỈ 1 CLICK): hỏi user có muốn UPDATE 1 Fitting Table đã có sẵn
                // không — bằng cách CLICK CHỌN Title (hoặc bất kỳ entity nào) của bảng đó (không tự
                // động quét/đoán cả bản vẽ, tránh xóa nhầm bảng khác). Enter/không chọn gì -> coi như
                // chèn bảng MỚI (vẫn hỏi điểm chèn như cũ, xem bên dưới).
                PromptEntityOptions peo = new PromptEntityOptions(
                    "\nSelect the Fitting Table's Title to update it in place (or press ENTER to insert a new table): ");
                peo.AllowNone = true;
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status == PromptStatus.Cancel)
                {
                    Debug.WriteLine($"{LOG_PREFIX} InsertFittingTable bị hủy (ở bước chọn bảng cũ để update).");
                    return null;
                }

                string oldTableGuid = null;
                string oldCreatedIso = null;
                Point3d? oldInsertPoint = null;
                if (per.Status == PromptStatus.OK)
                {
                    using (var trPeek = db.TransactionManager.StartTransaction())
                    {
                        if (trPeek.GetObject(per.ObjectId, OpenMode.ForRead) is Entity pickedEnt)
                        {
                            var meta = TryGetTableMetadata(pickedEnt);
                            oldTableGuid = meta.guid;
                            oldCreatedIso = meta.createdIso;
                            oldInsertPoint = meta.point;
                        }
                        trPeek.Commit();
                    }
                    if (oldTableGuid == null)
                        ed.WriteMessage("\nSelected entity does not belong to a Fitting Table — inserting a new table instead.");
                    else
                        Debug.WriteLine($"{LOG_PREFIX} Sẽ update bảng cũ (Table ID={oldTableGuid}).");
                }

                // 1-CLICK UPDATE: nếu đã xác định được bảng cũ VÀ nó có lưu điểm chèn gốc, dùng LUÔN vị
                // trí đó — KHÔNG hỏi lại điểm chèn mới. Chỉ hỏi điểm chèn khi chèn bảng MỚI hoàn toàn
                // (hoặc bảng cũ tạo trước khi có tính năng lưu điểm chèn, thiếu dữ liệu này).
                Point3d insertPoint;
                if (oldTableGuid != null && oldInsertPoint.HasValue)
                {
                    insertPoint = oldInsertPoint.Value;
                    Debug.WriteLine($"{LOG_PREFIX} Update-in-place: dùng lại điểm chèn gốc ({insertPoint.X:F2}, {insertPoint.Y:F2}) — không hỏi lại.");
                }
                else
                {
                    PromptPointOptions ppo = new PromptPointOptions(
                        $"\nSelect insertion point for Fitting Table ({rows.Count} fitting(s)) (or press ESC to cancel): ");
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status != PromptStatus.OK)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} InsertFittingTable bị hủy.");
                        return null;
                    }
                    insertPoint = ppr.Value;
                }

                // ==== Views luôn chèn ở TỈ LỆ THẬT 1:1 (không scale block) — theo yêu cầu user, Scale
                // trong Properties palette phải luôn = 1. Text/cột vẫn tính TỈ LỆ theo kích thước view
                // thật (median height, "textBasis") để chữ không quá bé/to so với hình chiếu thật —
                // KHÔNG dùng hằng số mm cố định — nhưng textBasis chỉ dùng để tính cỡ CHỮ, không còn
                // dùng để ép rowHeight/scale block như trước (bug đã báo — block bị scale ngoài ý muốn). ====
                var naturalHeights = viewSizes.Values.Select(v => v.height).Where(h => h > 0).OrderBy(h => h).ToList();
                double refHeight = naturalHeights.Count > 0 ? naturalHeights[naturalHeights.Count / 2] : 200.0; // median
                double textBasis = Math.Max(refHeight * 1.25, 30.0);

                double textHeight = Clamp(textBasis * 0.07, 2.5, textBasis * 0.18);
                // Header to hơn HẲN text thường (trước chỉ *1.15, không đủ tương phản) để tự thân
                // kích thước đã "nổi bật" mà không cần dựa vào mã định dạng bold (xem lý do bỏ \b1; bên dưới).
                double headerTextHeight = textHeight * 1.4;
                double cellPad = textHeight * 1.1;
                double headerHeight = headerTextHeight + cellPad * 2.4;
                double charW = textHeight * 0.65; // xấp xỉ độ rộng 1 ký tự MText font Standard
                double viewGap = cellPad * 2; // khoảng cách giữa các view trong cùng 1 ô Views — cố định, không phụ thuộc rowHeight (giờ rowHeight biến thiên theo từng hàng)
                double titleTextHeight = headerTextHeight * 2.4; // Table Title — nổi bật RÕ RỆT so với header
                double titlePad = cellPad * 2;
                double titleHeight = titleTextHeight + titlePad * 2;
                double createdTextHeight = textHeight * 0.85; // dòng "Created" cuối bảng — nhỏ, không cần nổi bật
                double createdPad = cellPad * 0.6;

                // rowHeights[r]: chiều cao THẬT của hàng r = chiều cao view lớn nhất (kích thước gốc,
                // KHÔNG scale) + padding — mỗi hàng cao thấp khác nhau tuỳ kích thước fitting thật.
                var rowHeights = new double[rows.Count];
                double viewsColWidth = 0;
                for (int r = 0; r < rows.Count; r++)
                {
                    var sizes = rows[r].Where(v => viewSizes.ContainsKey(v)).Select(v => viewSizes[v]).ToList();
                    double maxH = sizes.Count > 0 ? sizes.Max(s => s.height) : 0;
                    rowHeights[r] = maxH > 0 ? maxH + cellPad * 2 : Math.Max(textBasis * 0.5, 20.0);

                    double totalW = sizes.Sum(s => s.width);
                    if (sizes.Count > 1) totalW += (sizes.Count - 1) * viewGap;
                    viewsColWidth = Math.Max(viewsColWidth, totalW);
                }

                // Header dùng chữ HOA — độ rộng cột phải đủ cho CHÍNH header đó (biên an toàn 30%) LẪN
                // độ dài THẬT của dữ liệu trong cột đó (biên an toàn 15%, xem ColWidthFor) — không còn
                // dùng hằng số ước lượng cố định (bug đã báo: cột không đủ rộng chứa hết chữ thật, chữ
                // đè lên đường lưới). Cap tối đa (30 ký tự thường, 40 Description) tránh 1 giá trị dữ
                // liệu dài bất thường kéo cả bảng rộng quá mức — dài hơn cap thì MText tự wrap.
                // Views là CỘT ĐẦU TIÊN (theo yêu cầu user); Vault name=BlockName, Part ID=PartNumber,
                // X.Class=Title (khớp đúng ý nghĩa "XClass" đang dùng trong BOM harvest — mainCatItem.Title).
                string[] headersUpper = { "VIEWS", "POS.", "VAULT NAME", "PART ID", "X.CLASS", "DESCRIPTION", "WEIGHT", "DESIGNER" };
                int MaxDataLen(Func<CatalogItem, string> sel, int cap) =>
                    Math.Min(cap, rows.Select(r => (sel(r[0]) ?? "").Length).DefaultIfEmpty(0).Max());

                double colPos      = ColWidthFor(headersUpper[1], MaxDataLen(x => x.ProjectPosNum, 10), charW, cellPad);
                double colVault    = ColWidthFor(headersUpper[2], MaxDataLen(x => x.BlockName, 30), charW, cellPad);
                double colPartId   = ColWidthFor(headersUpper[3], MaxDataLen(x => x.PartNumber, 30), charW, cellPad);
                double colXClass   = ColWidthFor(headersUpper[4], MaxDataLen(x => x.Title, 30), charW, cellPad);
                double colDesc     = ColWidthFor(headersUpper[5], MaxDataLen(x => x.Description, 40), charW, cellPad);
                double colWeight   = ColWidthFor(headersUpper[6], MaxDataLen(x => (x.Mass ?? "0") + " kg", 12), charW, cellPad);
                double colDesigner = ColWidthFor(headersUpper[7], MaxDataLen(x => x.Designer, 24), charW, cellPad);
                viewsColWidth = Math.Max(viewsColWidth + cellPad * 2, colPos);

                double[] colWidths = { viewsColWidth, colPos, colVault, colPartId, colXClass, colDesc, colWeight, colDesigner };
                double tableWidth = colWidths.Sum();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        FittingBlockUtility.CheckAndCreateLayer(db, tr, FITTING_TABLE_LAYER, FITTING_TABLE_LAYER_COLOR);
                        EnsureRegApp(db, tr, FITTING_TABLE_XDATA_APP);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                        // Update-in-place: nếu user đã chọn 1 entity thuộc bảng cũ ở bước trên, ĐỌC
                        // snapshot dữ liệu từng hàng của bảng cũ (nếu có) TRƯỚC khi xóa — dùng để so
                        // sánh phát hiện hàng thay đổi (tô đỏ) — rồi xóa TOÀN BỘ entity mang cùng Table
                        // ID đó. Cùng transaction với thao tác vẽ nên nếu có lỗi giữa chừng, bảng cũ
                        // KHÔNG bị mất (rollback cả 2).
                        Dictionary<string, string> oldSnapshot = null;
                        int erasedOldCount = 0;
                        if (oldTableGuid != null)
                        {
                            oldSnapshot = TryReadOldSnapshot(tr, ms, oldTableGuid);
                            erasedOldCount = EraseEntitiesByTableGuid(tr, ms, oldTableGuid);
                            Debug.WriteLine($"{LOG_PREFIX} Đã xóa {erasedOldCount} entity của Fitting Table cũ (Table ID={oldTableGuid}) trước khi vẽ bảng mới. Snapshot cũ: {(oldSnapshot == null ? "không có" : $"{oldSnapshot.Count} hàng")}.");
                        }

                        string tableGuid = Guid.NewGuid().ToString();
                        string createdDateIso = oldCreatedIso ?? DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                        var allTableEntityIds = new List<ObjectId>();

                        double left = insertPoint.X;
                        double tableTop = insertPoint.Y;
                        double top = tableTop - titleHeight; // header row dịch xuống, nhường chỗ cho Title
                        double z = insertPoint.Z;

                        // Table Title — tên Category user đang chọn lúc Insert (vì 1 bản vẽ có thể
                        // chứa NHIỀU Fitting Table, mỗi bảng ứng với 1 category khác nhau). Chữ trắng
                        // (theo yêu cầu user — không thêm nền).
                        string titleText = string.IsNullOrEmpty(tableTitle) ? "FITTING TABLE" : $"FITTING TABLE — {tableTitle}";
                        allTableEntityIds.Add(AddHeaderText(tr, ms, titleText, left, top, tableWidth, titleHeight, titleTextHeight, z,
                            color: Color.FromRgb(255, 255, 255)));

                        // Header row — nền tô xám nhạt (Solid) để "highlight" thật sự thay vì dựa vào
                        // mã định dạng MText \b1; (KHÔNG hợp lệ khi đứng một mình, gây hiển thị lỗi
                        // "1;**" trước mỗi header — bug đã báo). Chữ HOA + to hơn + canh giữa ô.
                        Solid headerBg = new Solid(
                            new Point3d(left, top - headerHeight, z),               // p1: bottom-left
                            new Point3d(left + tableWidth, top - headerHeight, z),  // p2: bottom-right
                            new Point3d(left, top, z),                             // p3: top-left
                            new Point3d(left + tableWidth, top, z));               // p4: top-right
                        headerBg.Color = Color.FromColorIndex(ColorMethod.ByAci, 9); // ACI 9 — xám nhạt chuẩn
                        headerBg.Layer = FITTING_TABLE_LAYER;
                        ms.AppendEntity(headerBg);
                        tr.AddNewlyCreatedDBObject(headerBg, true);
                        allTableEntityIds.Add(headerBg.ObjectId);

                        double curX = left;
                        foreach (int c in Enumerable.Range(0, headersUpper.Length))
                        {
                            allTableEntityIds.Add(AddHeaderText(tr, ms, headersUpper[c], curX, top - headerHeight, colWidths[c], headerHeight, headerTextHeight, z));
                            curX += colWidths[c];
                        }

                        double rowTop = top - headerHeight;

                        // Ghi lại: ranh giới ô Views thật (dùng cho báo cáo chẩn đoán) + view vừa chèn từng hàng
                        var cellBoundsByRow = new (double left, double right, double top, double bottom)[rows.Count];
                        var createdViewsByRow = new List<(string blockName, ObjectId id)>[rows.Count];
                        var newSnapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var changedPartNumbers = new List<string>();

                        for (int r = 0; r < rows.Count; r++)
                        {
                            CatalogItem rep = rows[r][0];
                            double rowBottom = rowTop - rowHeights[r];
                            curX = left;

                            // So sánh với snapshot bảng cũ (nếu có) — hàng MỚI hoặc dữ liệu KHÁC so với
                            // lần trước sẽ được tô ĐỎ để user biết ngay chỗ nào vừa Update.
                            string rowValue = BuildRowSnapshotValue(rep, rows[r]);
                            newSnapshot[rep.PartNumber] = rowValue;
                            bool isChanged = oldSnapshot != null &&
                                (!oldSnapshot.TryGetValue(rep.PartNumber, out string oldValue) || oldValue != rowValue);
                            if (isChanged) changedPartNumbers.Add(rep.PartNumber);
                            Color rowColor = isChanged ? Color.FromColorIndex(ColorMethod.ByAci, 1) : null; // ACI 1 = đỏ

                            // Ô "Views" — CỘT ĐẦU TIÊN theo yêu cầu user. Canh CHÍNH XÁC theo local extents
                            // từng view (minX/minY), không phải đoán origin nằm ở góc nào — tránh chồng lấn
                            // lên đường lưới (bug đã báo).
                            double cellLeft = curX, cellRight = curX + colWidths[0], cellTop = rowTop, cellBottom = rowBottom;
                            cellBoundsByRow[r] = (cellLeft, cellRight, cellTop, cellBottom);
                            createdViewsByRow[r] = new List<(string, ObjectId)>();

                            // Chèn view ở TỈ LỆ THẬT 1:1 (không truyền scale — InsertBlockReference mặc
                            // định scale=1.0) — theo yêu cầu user, Scale trong Properties palette phải
                            // luôn = 1.
                            double vx = cellLeft + cellPad;      // biên trái MONG MUỐN của hàng view
                            double vyBottom = cellBottom + cellPad; // biên dưới MONG MUỐN của hàng view
                            foreach (var v in rows[r])
                            {
                                if (!viewSizes.TryGetValue(v, out var sz)) continue;
                                double insertX = vx - sz.minX;
                                double insertY = vyBottom - sz.minY;
                                ObjectId brId = FittingBlockUtility.InsertBlockReference(db, tr, sz.btrId, new Point3d(insertX, insertY, z));
                                createdViewsByRow[r].Add((v.BlockName, brId));
                                allTableEntityIds.Add(brId);
                                vx += sz.width + viewGap;
                            }
                            curX += colWidths[0];

                            allTableEntityIds.Add(AddCellText(tr, ms, rep.ProjectPosNum ?? "", curX + cellPad, rowTop - cellPad, colWidths[1] - cellPad * 2, textHeight, z, rowColor));
                            curX += colWidths[1];
                            allTableEntityIds.Add(AddCellText(tr, ms, rep.BlockName ?? "", curX + cellPad, rowTop - cellPad, colWidths[2] - cellPad * 2, textHeight, z, rowColor));
                            curX += colWidths[2];
                            allTableEntityIds.Add(AddCellText(tr, ms, rep.PartNumber ?? "", curX + cellPad, rowTop - cellPad, colWidths[3] - cellPad * 2, textHeight, z, rowColor));
                            curX += colWidths[3];
                            allTableEntityIds.Add(AddCellText(tr, ms, rep.Title ?? "", curX + cellPad, rowTop - cellPad, colWidths[4] - cellPad * 2, textHeight, z, rowColor));
                            curX += colWidths[4];
                            allTableEntityIds.Add(AddCellText(tr, ms, rep.Description ?? "", curX + cellPad, rowTop - cellPad, colWidths[5] - cellPad * 2, textHeight, z, rowColor));
                            curX += colWidths[5];
                            allTableEntityIds.Add(AddCellText(tr, ms, (rep.Mass ?? "0") + " kg", curX + cellPad, rowTop - cellPad, colWidths[6] - cellPad * 2, textHeight, z, rowColor));
                            curX += colWidths[6];
                            allTableEntityIds.Add(AddCellText(tr, ms, rep.Designer ?? "", curX + cellPad, rowTop - cellPad, colWidths[7] - cellPad * 2, textHeight, z, rowColor));

                            rowTop = rowBottom;
                        }

                        double bottom = rowTop;
                        allTableEntityIds.AddRange(DrawScheduleGrid(tr, ms, left, top, bottom, colWidths, headerHeight, rowHeights, z));

                        // Dòng "Created" — cuối bảng, nhỏ + xám, giữ nguyên ngày tạo GỐC qua các lần Update.
                        DateTime createdDisplay = DateTime.TryParse(createdDateIso, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out DateTime parsedCreated) ? parsedCreated : DateTime.Now;
                        string createdLabel = $"Created: {createdDisplay:yyyy-MM-dd HH:mm}";
                        allTableEntityIds.Add(AddCellText(tr, ms, createdLabel, left, bottom - createdPad, tableWidth, createdTextHeight, z,
                            Color.FromColorIndex(ColorMethod.ByAci, 8))); // ACI 8 — xám đậm, không nổi bật

                        // Gắn metadata (Table ID + Created GỐC + điểm chèn GỐC) lên MỌI entity vừa vẽ —
                        // dùng để nhận diện + xóa đúng bảng này VÀ vẽ lại đúng vị trí khi user Update ở
                        // lần chạy sau (xem bước chọn entity bảng cũ phía trên — chỉ 1 click).
                        foreach (ObjectId id in allTableEntityIds)
                        {
                            if (tr.GetObject(id, OpenMode.ForWrite) is Entity taggedEnt)
                                TagTableMetadata(taggedEnt, tableGuid, createdDateIso, insertPoint);
                        }

                        // Lưu snapshot dữ liệu từng hàng (Xrecord trong Extension Dictionary của
                        // headerBg) — dùng để so sánh tô đỏ ở lần Update TIẾP THEO.
                        SaveRowSnapshot(tr, headerBg, newSnapshot);

                        // Báo cáo chẩn đoán — đọc lại GeometricExtents THẬT của từng view vừa chèn (còn
                        // trong transaction, trước commit) để kiểm tra có tràn ra khỏi ô/lưới không.
                        var removedPartNumbers = oldSnapshot != null
                            ? oldSnapshot.Keys.Except(newSnapshot.Keys, StringComparer.OrdinalIgnoreCase).ToList()
                            : new List<string>();
                        string reportPath = BuildDiagnosticReport(
                            tr, insertPoint, refHeight, rowHeights, headerHeight, textHeight, headerTextHeight, cellPad,
                            colWidths, tableWidth, left, top, bottom, rows, cellBoundsByRow, createdViewsByRow,
                            tableGuid, oldTableGuid, erasedOldCount, tableTitle, createdDisplay, changedPartNumbers, removedPartNumbers);

                        tr.Commit();
                        Debug.WriteLine($"{LOG_PREFIX} InsertFittingTable THÀNH CÔNG ({rows.Count} fitting(s)). Report: {reportPath}");
                        return reportPath;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (InsertFittingTable): {ex.Message}");
                        throw;
                    }
                }
            }
        }

        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private static int ParsePosForSort(string pos) => int.TryParse(pos, out int n) ? n : int.MaxValue;

        /// <summary>Tính extents CỤC BỘ (chưa transform, theo hệ toạ độ riêng của block) của toàn bộ
        /// entity hình học (bỏ qua AttributeDefinition) trong 1 BlockTableRecord.</summary>
        private static bool TryComputeLocalExtents(Transaction tr, BlockTableRecord btr, out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = double.MaxValue; minY = double.MaxValue;
            maxX = double.MinValue; maxY = double.MinValue;
            foreach (ObjectId id in btr)
            {
                try
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ent is AttributeDefinition) continue;
                    Extents3d ext = ent.GeometricExtents;
                    if (ext.MinPoint.X < minX) minX = ext.MinPoint.X;
                    if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                    if (ext.MinPoint.Y < minY) minY = ext.MinPoint.Y;
                    if (ext.MaxPoint.Y > maxY) maxY = ext.MaxPoint.Y;
                }
                catch { }
            }
            return minX < double.MaxValue && maxX > double.MinValue;
        }

        /// <summary>Chèn 1 ô text (MText) thường — canh top-left, dùng cho các ô dữ liệu.
        /// <paramref name="color"/> null = giữ màu mặc định (ByLayer); truyền màu cụ thể để tô nổi bật
        /// (vd đỏ cho hàng vừa thay đổi khi Update).</summary>
        private static ObjectId AddCellText(Transaction tr, BlockTableRecord ms, string text, double x, double yTop,
            double width, double textHeight, double z, Color color = null)
        {
            MText mt = new MText();
            mt.SetDatabaseDefaults();
            mt.Location = new Point3d(x, yTop, z);
            mt.Width = Math.Max(width, 1);
            mt.TextHeight = textHeight;
            mt.Contents = EscapeMText(text);
            mt.Attachment = AttachmentPoint.TopLeft;
            mt.Layer = FITTING_TABLE_LAYER;
            if (color != null) mt.Color = color;
            ms.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
            return mt.ObjectId;
        }

        /// <summary>Chèn text HEADER/TITLE — canh GIỮA ô (ngang + dọc). KHÔNG dùng mã định dạng MText
        /// \b1; (không hợp lệ khi đứng một mình, gây hiển thị lỗi "1;**" — bug đã báo) — "nổi bật" hoàn
        /// toàn nhờ nền Solid (header, xem InsertFittingTable) + cỡ chữ lớn hơn + viết hoa + màu riêng.
        /// <paramref name="color"/> null = đen tuyệt đối (mặc định cho Header); Title truyền màu trắng riêng.</summary>
        private static ObjectId AddHeaderText(Transaction tr, BlockTableRecord ms, string text, double cellLeft, double cellBottom,
            double cellWidth, double cellHeight, double textHeight, double z, Color color = null)
        {
            MText mt = new MText();
            mt.SetDatabaseDefaults();
            mt.Location = new Point3d(cellLeft + cellWidth / 2.0, cellBottom + cellHeight / 2.0, z);
            mt.Width = Math.Max(cellWidth - 2, 1);
            mt.TextHeight = textHeight;
            mt.Contents = EscapeMText(text);
            mt.Attachment = AttachmentPoint.MiddleCenter;
            mt.Layer = FITTING_TABLE_LAYER;
            mt.Color = color ?? Color.FromRgb(0, 0, 0); // mặc định đen (header) — Title truyền màu riêng
            ms.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);
            return mt.ObjectId;
        }

        /// <summary>Escape ký tự đặc biệt MText (\, {, }) trong text người dùng nhập tự do (Description...).</summary>
        private static string EscapeMText(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

        /// <summary>Đảm bảo RegApp <paramref name="appName"/> tồn tại trong AppId table — bắt buộc phải
        /// đăng ký trước khi gán XData cho entity, nếu không sẽ lỗi khi set <see cref="Entity.XData"/>.</summary>
        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(appName)) return;
            rat.UpgradeOpen();
            RegAppTableRecord ratr = new RegAppTableRecord { Name = appName };
            rat.Add(ratr);
            tr.AddNewlyCreatedDBObject(ratr, true);
        }

        /// <summary>Gắn metadata (Table ID GUID + ngày Created GỐC ISO + điểm chèn GỐC) lên 1 entity
        /// qua XData — 3 chuỗi có prefix ("GUID=", "CREATED=", "POINT=") để đọc lại KHÔNG phụ thuộc thứ
        /// tự. Dùng để nhận diện toàn bộ entity thuộc CÙNG 1 lần vẽ Fitting Table (update-in-place) VÀ
        /// biết vẽ lại đúng vị trí/giữ nguyên ngày tạo khi Update (chỉ 1 click, không hỏi lại điểm chèn).</summary>
        private static void TagTableMetadata(Entity ent, string tableGuid, string createdDateIso, Point3d insertPoint)
        {
            string pointStr = string.Join(",",
                insertPoint.X.ToString(CultureInfo.InvariantCulture),
                insertPoint.Y.ToString(CultureInfo.InvariantCulture),
                insertPoint.Z.ToString(CultureInfo.InvariantCulture));
            using (ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FITTING_TABLE_XDATA_APP),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "GUID=" + tableGuid),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "CREATED=" + createdDateIso),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "POINT=" + pointStr)))
            {
                ent.XData = rb;
            }
        }

        /// <summary>Đọc lại metadata (Table ID/Created/điểm chèn) từ XData của 1 entity — mọi giá trị
        /// null nếu entity không thuộc Fitting Table nào (dùng khi user click chọn entity của bảng cũ
        /// để update).</summary>
        private static (string guid, string createdIso, Point3d? point) TryGetTableMetadata(Entity ent)
        {
            string guid = null, createdIso = null;
            Point3d? point = null;
            using (ResultBuffer rb = ent.GetXDataForApplication(FITTING_TABLE_XDATA_APP))
            {
                if (rb == null) return (null, null, null);
                foreach (TypedValue tv in rb.AsArray())
                {
                    if (tv.TypeCode != (int)DxfCode.ExtendedDataAsciiString) continue;
                    string s = tv.Value as string;
                    if (string.IsNullOrEmpty(s)) continue;

                    if (s.StartsWith("GUID=", StringComparison.Ordinal)) guid = s.Substring(5);
                    else if (s.StartsWith("CREATED=", StringComparison.Ordinal)) createdIso = s.Substring(8);
                    else if (s.StartsWith("POINT=", StringComparison.Ordinal))
                    {
                        var parts = s.Substring(6).Split(',');
                        if (parts.Length == 3
                            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double px)
                            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double py)
                            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double pz))
                            point = new Point3d(px, py, pz);
                    }
                }
            }
            return (guid, createdIso, point);
        }

        /// <summary>Xóa toàn bộ entity trong <paramref name="ms"/> mang cùng Table ID
        /// <paramref name="tableGuid"/> — dùng để dọn bảng cũ trước khi vẽ bảng mới (update-in-place).
        /// Trả về số entity đã xóa.</summary>
        private static int EraseEntitiesByTableGuid(Transaction tr, BlockTableRecord ms, string tableGuid)
        {
            int count = 0;
            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                if (TryGetTableMetadata(ent).guid != tableGuid) continue;
                ent.UpgradeOpen();
                ent.Erase();
                count++;
            }
            return count;
        }

        /// <summary>Duyệt <paramref name="ms"/> tìm entity đầu tiên mang <paramref name="tableGuid"/> có
        /// lưu snapshot dữ liệu hàng (thường là headerBg cũ) — đọc TRƯỚC khi xóa bảng cũ, dùng để so
        /// sánh phát hiện hàng thay đổi (tô đỏ). Trả về null nếu bảng cũ chưa từng lưu snapshot (vd tạo
        /// trước khi có tính năng này).</summary>
        private static Dictionary<string, string> TryReadOldSnapshot(Transaction tr, BlockTableRecord ms, string tableGuid)
        {
            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                if (TryGetTableMetadata(ent).guid != tableGuid) continue;
                var snapshot = TryReadRowSnapshot(tr, ent);
                if (snapshot != null) return snapshot;
            }
            return null;
        }

        /// <summary>Ghép dữ liệu 1 hàng thành 1 chuỗi để so sánh phát hiện thay đổi — gồm mọi field
        /// hiển thị trên bảng CAD (Pos/BlockName/PartNumber/Title/Description/Mass/Designer) + danh
        /// sách BlockName của mọi view trong hàng (đã sort, phát hiện được cả trường hợp thêm/bớt view).</summary>
        private static string BuildRowSnapshotValue(CatalogItem rep, List<CatalogItem> viewsInRow)
        {
            string viewNames = string.Join(",", viewsInRow.Select(v => v.BlockName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            return string.Join("|", rep.ProjectPosNum, rep.BlockName, rep.PartNumber, rep.Title, rep.Description, rep.Mass, rep.Designer, viewNames);
        }

        /// <summary>Lưu snapshot dữ liệu từng hàng (JSON qua Newtonsoft) vào Xrecord trong Extension
        /// Dictionary của <paramref name="ent"/> — dùng Xrecord (không phải XData) vì không giới hạn
        /// 255 ký tự/chuỗi, phù hợp lưu JSON cho nhiều hàng. <paramref name="ent"/> phải đang mở
        /// ForWrite (đúng ngay sau khi tạo mới, xem InsertFittingTable).</summary>
        private static void SaveRowSnapshot(Transaction tr, Entity ent, Dictionary<string, string> snapshot)
        {
            string json = JsonConvert.SerializeObject(snapshot);
            if (ent.ExtensionDictionary == ObjectId.Null)
                ent.CreateExtensionDictionary();
            DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            if (extDict.Contains(ROW_SNAPSHOT_DICT_KEY))
            {
                var existing = (Xrecord)tr.GetObject(extDict.GetAt(ROW_SNAPSHOT_DICT_KEY), OpenMode.ForWrite);
                existing.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, json));
            }
            else
            {
                var xrec = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, json)) };
                extDict.SetAt(ROW_SNAPSHOT_DICT_KEY, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
        }

        /// <summary>Đọc lại snapshot dữ liệu từng hàng đã lưu (nếu có) — trả về null nếu entity không
        /// có Extension Dictionary hoặc không có key snapshot (bảng cũ tạo trước khi có tính năng này).</summary>
        private static Dictionary<string, string> TryReadRowSnapshot(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary == ObjectId.Null) return null;
            DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
            if (!extDict.Contains(ROW_SNAPSHOT_DICT_KEY)) return null;
            if (!(tr.GetObject(extDict.GetAt(ROW_SNAPSHOT_DICT_KEY), OpenMode.ForRead) is Xrecord xrec)) return null;
            var tv = xrec.Data?.AsArray()?.FirstOrDefault();
            string json = tv?.Value as string;
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<Dictionary<string, string>>(json); }
            catch { return null; }
        }

        /// <summary>Tính độ rộng cột đủ cho CẢ header (chữ HOA, biên an toàn 30% vì charW chỉ là ước
        /// lượng font) LẪN dữ liệu THẬT (biên an toàn 15% — nhẹ hơn header vì đã lấy đúng độ dài dữ liệu
        /// thật thay vì đoán, nhưng vẫn cần chút biên phòng charW ước lượng lệch so với font thật) — lấy
        /// max, tránh chữ (header hoặc dữ liệu) xuống dòng đè lên lưới.</summary>
        private static double ColWidthFor(string headerUpper, int dataCharEstimate, double charW, double cellPad)
        {
            int headerChars = (int)Math.Ceiling(headerUpper.Length * 1.3);
            int dataChars = (int)Math.Ceiling(dataCharEstimate * 1.15);
            int chars = Math.Max(dataChars, headerChars);
            return charW * chars + cellPad * 2;
        }

        /// <summary>Vẽ lưới: đường ngang mỗi hàng (header + N hàng dữ liệu, chiều cao từng hàng lấy từ
        /// <paramref name="rowHeights"/> — biến thiên theo kích thước view thật, không cố định — + đáy)
        /// và đường dọc mỗi biên cột, tạo cảm giác 1 bảng thật thay vì các ô rời rạc.</summary>
        private static List<ObjectId> DrawScheduleGrid(Transaction tr, BlockTableRecord ms, double left, double top, double bottom,
            double[] colWidths, double headerHeight, double[] rowHeights, double z)
        {
            var lineIds = new List<ObjectId>();
            double right = left + colWidths.Sum();

            var horizontalYs = new List<double> { top, top - headerHeight };
            double y = top - headerHeight;
            foreach (var rh in rowHeights)
            {
                y -= rh;
                horizontalYs.Add(y);
            }
            foreach (var hy in horizontalYs)
                lineIds.Add(AddLine(tr, ms, new Point3d(left, hy, z), new Point3d(right, hy, z)));

            double x = left;
            lineIds.Add(AddLine(tr, ms, new Point3d(x, top, z), new Point3d(x, bottom, z)));
            foreach (var w in colWidths)
            {
                x += w;
                lineIds.Add(AddLine(tr, ms, new Point3d(x, top, z), new Point3d(x, bottom, z)));
            }
            return lineIds;
        }

        private static ObjectId AddLine(Transaction tr, BlockTableRecord ms, Point3d p1, Point3d p2)
        {
            Line line = new Line(p1, p2);
            line.Layer = FITTING_TABLE_LAYER;
            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            return line.ObjectId;
        }

        /// <summary>
        /// Ghi file .txt chẩn đoán bảng vừa chèn — kích thước từng thành phần + kiểm tra overlap
        /// của từng view thật (GeometricExtents đọc lại từ DB) với ranh giới ô Views/lưới + danh sách
        /// hàng vừa thay đổi/hàng bị xóa (nếu là Update). Mục đích: user gửi lại file này thay vì ảnh
        /// chụp màn hình để đánh giá chất lượng bảng.
        /// </summary>
        private string BuildDiagnosticReport(
            Transaction tr, Point3d insertPoint, double refHeight, double[] rowHeights, double headerHeight,
            double textHeight, double headerTextHeight, double cellPad, double[] colWidths, double tableWidth,
            double left, double top, double bottom,
            List<List<CatalogItem>> rows,
            (double left, double right, double top, double bottom)[] cellBoundsByRow,
            List<(string blockName, ObjectId id)>[] createdViewsByRow,
            string tableGuid, string oldTableGuid, int erasedOldCount,
            string tableTitle, DateTime created, List<string> changedPartNumbers, List<string> removedPartNumbers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== MCG Fitting Table — Diagnostic Report ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Table ID: {tableGuid}");
            sb.AppendLine($"Title: {(string.IsNullOrEmpty(tableTitle) ? "(none)" : tableTitle)}");
            sb.AppendLine($"Created: {created:yyyy-MM-dd HH:mm}");
            sb.AppendLine(oldTableGuid != null
                ? $"Mode: UPDATE existing table (old Table ID={oldTableGuid}, erased {erasedOldCount} old entity/entities)"
                : "Mode: INSERT new table");
            if (oldTableGuid != null)
            {
                sb.AppendLine(changedPartNumbers.Count > 0
                    ? $"Changed rows (tô đỏ): {string.Join(", ", changedPartNumbers)}"
                    : "Changed rows: none");
                if (removedPartNumbers.Count > 0)
                    sb.AppendLine($"Removed since last update (không còn trong bảng mới): {string.Join(", ", removedPartNumbers)}");
            }
            sb.AppendLine($"Insertion point: ({insertPoint.X:F2}, {insertPoint.Y:F2}, {insertPoint.Z:F2})");
            sb.AppendLine($"Reference view height (median): {refHeight:F2} mm");
            sb.AppendLine($"Views inserted at TRUE 1:1 scale (Block Scale luôn = 1 trong Properties palette) — row height BIẾN THIÊN theo kích thước view thật: min={rowHeights.Min():F2} mm, max={rowHeights.Max():F2} mm");
            sb.AppendLine($"Header height: {headerHeight:F2} mm");
            sb.AppendLine($"Text height: {textHeight:F2} mm | Header text height: {headerTextHeight:F2} mm | Cell padding: {cellPad:F2} mm");
            sb.AppendLine($"Column widths (mm): Views={colWidths[0]:F1} | Pos.={colWidths[1]:F1} | Vault Name={colWidths[2]:F1} | " +
                $"Part ID={colWidths[3]:F1} | X.Class={colWidths[4]:F1} | Description={colWidths[5]:F1} | Weight={colWidths[6]:F1} | Designer={colWidths[7]:F1}");
            sb.AppendLine($"Table bounds: X[{left:F2}, {(left + tableWidth):F2}]  Y[{bottom:F2}, {top:F2}]  (Width={tableWidth:F2}, Height={(top - bottom):F2})");
            sb.AppendLine();

            int overlapCount = 0;
            int totalViews = 0;
            const double TOL = 0.05; // mm — sai số cho phép trước khi tính là overlap thật

            for (int r = 0; r < rows.Count; r++)
            {
                CatalogItem rep = rows[r][0];
                var cell = cellBoundsByRow[r];
                sb.AppendLine($"--- Row {r + 1}: {rep.PartNumber} (Pos {(string.IsNullOrEmpty(rep.ProjectPosNum) ? "-" : rep.ProjectPosNum)}) — {rows[r].Count} view(s), row height={rowHeights[r]:F2} mm ---");
                sb.AppendLine($"  Views cell bounds (grid): X[{cell.left:F2}, {cell.right:F2}]  Y[{cell.bottom:F2}, {cell.top:F2}]");

                foreach (var (blockName, id) in createdViewsByRow[r])
                {
                    totalViews++;
                    Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    Extents3d ext;
                    try { ext = ent.GeometricExtents; }
                    catch
                    {
                        sb.AppendLine($"  View '{blockName}': KHÔNG đọc được extents (bỏ qua check overlap)");
                        continue;
                    }

                    bool overflowLeft = ext.MinPoint.X < cell.left - TOL;
                    bool overflowRight = ext.MaxPoint.X > cell.right + TOL;
                    bool overflowBottom = ext.MinPoint.Y < cell.bottom - TOL;
                    bool overflowTop = ext.MaxPoint.Y > cell.top + TOL;
                    bool overlap = overflowLeft || overflowRight || overflowBottom || overflowTop;
                    if (overlap) overlapCount++;

                    string status = overlap
                        ? $"⚠ OVERLAP (L={overflowLeft} R={overflowRight} B={overflowBottom} T={overflowTop})"
                        : "OK";
                    sb.AppendLine($"  View '{blockName}': extents X[{ext.MinPoint.X:F2}, {ext.MaxPoint.X:F2}]  Y[{ext.MinPoint.Y:F2}, {ext.MaxPoint.Y:F2}]  -> {status}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("=== Summary ===");
            sb.AppendLine($"Total rows: {rows.Count}");
            sb.AppendLine($"Total views placed: {totalViews}");
            sb.AppendLine($"Views with overlap into grid lines: {overlapCount}");

            string reportPath = Path.Combine(_libraryFolderPath, "FittingTableDiagnostics.txt");
            try
            {
                if (!Directory.Exists(_libraryFolderPath)) Directory.CreateDirectory(_libraryFolderPath);
                File.WriteAllText(reportPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} Không ghi được diagnostic report: {ex.Message}");
            }
            return reportPath;
        }
    }
}
