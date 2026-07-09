using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using Microsoft.Win32;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;
using Autodesk.AutoCAD.DatabaseServices;

// GIẢI QUYẾT LỖI XUNG ĐỘT TÊN DATATABLE
using DataTable = System.Data.DataTable;
using DataRow = System.Data.DataRow;
using DataColumn = System.Data.DataColumn;
// GIẢI QUYẾT LỖI XUNG ĐỘT TÊN VISIBILITY (AutoCAD vs WPF)
using Visibility = System.Windows.Visibility;

namespace MCG_FittingManagement.Views.FittingManagement
{
    public enum BomMode { Equipment, Hull }

    public partial class BomPreviewWindow : Window
    {
        private const string LOG_PREFIX = "[BomPreviewWindow]";

        private readonly IFittingManagementService _service;
        private readonly BomMode _mode;
        private DataTable _bomDataTable;
        private List<BomHarvestRecord> _lastScanResults;

        public BomPreviewWindow(IFittingManagementService service, BomMode mode)
        {
            InitializeComponent();
            _service = service;
            _mode = mode;
            ApplyMode();
            InitializeEmptyGrid();

            UpdateActiveProjectLabel();
            ActiveProjectContext.Instance.ProjectChanged += OnActiveProjectChanged;
            this.Closed += (_, __) => ActiveProjectContext.Instance.ProjectChanged -= OnActiveProjectChanged;
        }

        private void OnActiveProjectChanged(object sender, EventArgs e) => Dispatcher.Invoke(UpdateActiveProjectLabel);

        private void UpdateActiveProjectLabel()
        {
            var ctx = ActiveProjectContext.Instance;
            TxtActiveProject.Text = ctx.HasActiveProject ? ctx.ProjectDisplayName : "(none)";
        }

        private void ApplyMode()
        {
            // Đồng bộ style Title Grid với Master/Item Library — title in-hoa + badge màu theo mode
            if (_mode == BomMode.Hull)
            {
                Title = "Hull BOM Export";
                TxtModeLabel.Text = "HULL BOM EXPORT";
                TxtBadgeMode.Text = "HULL";
                var hullColor = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
                TxtModeLabel.Foreground = hullColor;
                BadgeMode.Background = hullColor;
                BtnScanDrawing.Content = "Scan Hull from Drawing";
                BtnAutoBalloon.Visibility = Visibility.Collapsed;
                MenuItemSyncPosFromItemLib.Visibility = Visibility.Visible;
                // Hull Pos được Sync TỪ Item Library xuống (chiều Item Lib → drawing đã có sẵn ở
                // nút "Sync" bên Item Library) — nút Sync Pos to CAD (đẩy từ grid này ra) không còn
                // ý nghĩa cho Hull vì grid không còn là nguồn Pos nữa.
                BtnSyncPosToCad.Visibility = Visibility.Collapsed;
            }
            else
            {
                Title = "Equipment BOM Export";
                TxtModeLabel.Text = "EQUIPMENT BOM EXPORT";
                TxtBadgeMode.Text = "EQUIPMENT";
                var equipColor = new SolidColorBrush(Color.FromRgb(0x00, 0x55, 0xA5));
                TxtModeLabel.Foreground = equipColor;
                BadgeMode.Background = equipColor;
                BtnScanDrawing.Content = "Scan Equipment from Drawing";
                BtnAutoBalloon.Visibility = Visibility.Visible;
                MenuItemSyncPosFromItemLib.Visibility = Visibility.Collapsed;
                BtnSyncPosToCad.Visibility = Visibility.Visible;
            }
        }

        private void InitializeEmptyGrid()
        {
            _bomDataTable = new DataTable();
            _bomDataTable.Columns.Add("Vault Name", typeof(string));
            _bomDataTable.Columns.Add("Type", typeof(string));
            _bomDataTable.Columns.Add("Part ID", typeof(string));
            _bomDataTable.Columns.Add("XClass", typeof(string));
            _bomDataTable.Columns.Add("Description", typeof(string));
            GridBomMatrix.ItemsSource = _bomDataTable.DefaultView;
        }

        private void GridBomMatrix_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyName == "Vault Name" || e.PropertyName == "Type" || e.PropertyName == "Part ID" || e.PropertyName == "XClass" || e.PropertyName == "Description")
            {
                e.Column.IsReadOnly = true;
                if (e.PropertyName == "Description") e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                if (e.PropertyName == "Type") e.Column.Header = "Hierarchy";
            }
            else if (e.PropertyName.EndsWith(" Qty") || e.PropertyName.EndsWith(" Pos"))
            {
                bool isPosColumn = e.PropertyName.EndsWith(" Pos");
                // Hull: Pos được sửa trực tiếp ở Item Library (Auto-Assign Pos / Edit Properties) rồi
                // kéo qua bằng "Sync Pos from Item Lib" — không cho sửa tay trên grid này nữa.
                e.Column.IsReadOnly = !isPosColumn || _mode == BomMode.Hull;
                Style cellStyle = new Style(typeof(DataGridCell));
                var color = e.PropertyName.EndsWith(" Qty")
                    ? System.Windows.Media.Color.FromRgb(240, 248, 255)
                    : System.Windows.Media.Color.FromRgb(255, 250, 205);
                cellStyle.Setters.Add(new Setter(BackgroundProperty, new System.Windows.Media.SolidColorBrush(color)));
                cellStyle.Setters.Add(new Setter(ForegroundProperty, System.Windows.Media.Brushes.Black));
                e.Column.CellStyle = cellStyle;
            }
        }

        private void BtnScanDrawing_Click(object sender, RoutedEventArgs e)
        {
            // Bắt buộc có active project trước khi scan — BOM chỉ đếm block đã add vào Item Library
            var ctx = ActiveProjectContext.Instance;
            if (!ctx.HasActiveProject)
            {
                MessageBox.Show(
                    "Chưa có Active Project.\n\nMở tab Project Config → Item Library → load hoặc tạo project mới trước khi scan BOM.",
                    "No Active Project", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.Visibility = Visibility.Hidden;
            try
            {
                List<BomHarvestRecord> rawData;
                if (_mode == BomMode.Hull)
                {
                    rawData = _service.HarvestInterfaceBom();
                    if (rawData != null) OverlayHullPositions(rawData);
                }
                else
                {
                    rawData = _service.HarvestStructureBom();
                }

                // Chỉ giữ block đã được add vào active project (Item Library) — loại block copy tự do
                int rawCount = rawData?.Count ?? 0;
                if (rawData != null) rawData = FilterByProjectCatalog(rawData, ctx);
                int excludedCount = rawCount - (rawData?.Count ?? 0);

                if (rawData == null || rawData.Count == 0)
                {
                    TxtStatus.Text = excludedCount > 0
                        ? $"No valid blocks found in active project. ({excludedCount} block(s) excluded — not in Item Library)"
                        : "No valid blocks found.";
                    return;
                }

                int containerCount = rawData.Select(r => r.PanelName).Distinct().Count();
                RebuildBomTable(rawData);

                string excludedNote = excludedCount > 0 ? $"  ({excludedCount} excluded — not in Item Library)" : "";
                TxtStatus.Text = $"Scan complete. Found {containerCount} group(s).{excludedNote}";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "System Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { this.Visibility = Visibility.Visible; }
        }

        /// <summary>
        /// Build lại toàn bộ _bomDataTable (cột Qty/Pos theo container + row group theo fitting) từ
        /// <paramref name="data"/>. Dùng chung cho Scan (data mới harvest) và Sync Pos from Item Lib
        /// (data cũ, chỉ ProjectPosNum vừa được refresh) — tách riêng để không phải rescan CAD lúc Sync.
        /// </summary>
        private void RebuildBomTable(List<BomHarvestRecord> data)
        {
            _lastScanResults = data;
            _bomDataTable.Clear();
            _bomDataTable.Columns.Clear();

            string[] headers = { "Vault Name", "Type", "Part ID", "XClass", "Description" };
            foreach (var h in headers) _bomDataTable.Columns.Add(h, typeof(string));

            var uniqueContainers = data.Select(r => r.PanelName).Distinct().OrderBy(p => p).ToList();
            foreach (var container in uniqueContainers)
            {
                _bomDataTable.Columns.Add($"{container} Qty", typeof(int));
                _bomDataTable.Columns.Add($"{container} Pos", typeof(string));
            }

            var groupedFittings = data
                .GroupBy(r => new { r.VaultName, r.ParentPartId, r.IsAccessory })
                .OrderBy(g => g.Key.IsAccessory ? g.Key.ParentPartId : g.Key.VaultName)
                .ThenBy(g => g.Key.IsAccessory).ThenBy(g => g.Key.VaultName);

            foreach (var group in groupedFittings)
            {
                DataRow newRow = _bomDataTable.NewRow();
                newRow["Vault Name"]   = group.Key.VaultName;
                newRow["Type"]         = group.Key.IsAccessory ? $"  ↳ Acc. of {group.Key.ParentPartId}" : "Main Fitting";
                newRow["Part ID"]      = group.First().PartId ?? "";
                newRow["XClass"]       = group.First().XClass ?? "N/A";
                newRow["Description"]  = group.First().Description ?? "Harvested from CAD";

                foreach (var container in uniqueContainers)
                {
                    int totalQty = group.Where(r => r.PanelName == container).Sum(r => r.Quantity);
                    if (totalQty > 0)
                    {
                        newRow[$"{container} Qty"] = totalQty;
                        string posNum = group.First().ProjectPosNum;
                        newRow[$"{container} Pos"] = (!string.IsNullOrEmpty(posNum) && int.TryParse(posNum, out int pNum))
                            ? pNum.ToString("D3") : (posNum ?? "");
                        foreach (var r in group.Where(r => r.PanelName == container))
                            r.Position = newRow[$"{container} Pos"].ToString();
                    }
                    else
                    {
                        newRow[$"{container} Pos"] = "";
                    }
                }
                _bomDataTable.Rows.Add(newRow);
            }

            GridBomMatrix.ItemsSource = null;
            GridBomMatrix.ItemsSource = _bomDataTable.DefaultView;
        }

        /// <summary>
        /// Hull mode — kéo lại ProjectPosNum mới nhất từ Item Library (project catalog) xuống
        /// bảng đã scan, không cần rescan lại toàn bộ bản vẽ. Dùng sau khi user Auto-Assign Pos /
        /// Edit Properties (Pos Num) bên Item Library.
        /// </summary>
        private void BtnSyncPosFromItemLib_Click(object sender, RoutedEventArgs e)
        {
            if (_lastScanResults == null || _lastScanResults.Count == 0)
            {
                MessageBox.Show("Scan the drawing first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ctx = ActiveProjectContext.Instance;
            if (!ctx.HasActiveProject)
            {
                MessageBox.Show("No active project. Open Item Library and load/create one first.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OverlayHullPositions(_lastScanResults);
            RebuildBomTable(_lastScanResults);
            TxtStatus.Text = "Position synced from Item Library.";
        }

        /// <summary>
        /// Hull mode: đọc ProjectPosNum từ active project catalog (project JSON),
        /// vì master catalog không chứa pos đã assign trong Item Library.
        /// </summary>
        private void OverlayHullPositions(List<BomHarvestRecord> records)
        {
            var ctx = ActiveProjectContext.Instance;
            if (!ctx.HasActiveProject) return;

            try
            {
                var projectItems = CatalogJsonStore.Read<ProjectCatalogItem>(ctx.ProjectFilePath);
                var posLookup = projectItems
                    .Where(p => !string.IsNullOrEmpty(p.PartNumber) && !string.IsNullOrEmpty(p.ProjectPosNum))
                    .ToDictionary(p => p.PartNumber, p => p.ProjectPosNum, StringComparer.OrdinalIgnoreCase);

                foreach (var record in records)
                {
                    if (string.IsNullOrEmpty(record.PartId)) continue;
                    if (posLookup.TryGetValue(record.PartId, out string pos))
                        record.ProjectPosNum = pos;
                }
                Debug.WriteLine($"{LOG_PREFIX} OverlayHullPositions: matched against {posLookup.Count} project items.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} OverlayHullPositions error: {ex.Message}");
            }
        }

        /// <summary>
        /// Chỉ giữ lại record có PartId tồn tại trong active project catalog.
        /// Block trong drawing nhưng chưa add vào Item Library → loại khỏi BOM.
        /// </summary>
        private List<BomHarvestRecord> FilterByProjectCatalog(List<BomHarvestRecord> records, ActiveProjectContext ctx)
        {
            try
            {
                var projectItems = CatalogJsonStore.Read<ProjectCatalogItem>(ctx.ProjectFilePath);
                var validPartIds = new HashSet<string>(
                    projectItems.Select(p => p.PartNumber).Where(p => !string.IsNullOrEmpty(p)),
                    StringComparer.OrdinalIgnoreCase);

                var filtered = records
                    .Where(r => !string.IsNullOrEmpty(r.PartId) && validPartIds.Contains(r.PartId))
                    .ToList();

                Debug.WriteLine($"{LOG_PREFIX} FilterByProjectCatalog: {filtered.Count}/{records.Count} records kept.");
                return filtered;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} FilterByProjectCatalog error (fallback — no filter): {ex.Message}");
                return records;
            }
        }

        private void BtnAutoBalloon_Click(object sender, RoutedEventArgs e)
        {
            // Chỉ dùng cho Equipment mode (button bị ẩn ở Hull mode)
            if (_lastScanResults == null || _lastScanResults.Count == 0) return;
            var uniqueContainers = _lastScanResults.Select(r => r.PanelName).Distinct().ToList();

            foreach (var container in uniqueContainers)
            {
                int posCounter = 1;
                var recordsInContainer = _lastScanResults
                    .Where(r => r.PanelName == container)
                    .OrderBy(r => r.IsAccessory ? r.ParentPartId : r.VaultName)
                    .ThenBy(r => r.IsAccessory)
                    .GroupBy(r => new { r.VaultName, r.ParentPartId, r.IsAccessory })
                    .ToList();

                foreach (var group in recordsInContainer)
                {
                    string finalPos = posCounter.ToString("D3");
                    foreach (var record in group) record.Position = finalPos;

                    foreach (DataRow row in _bomDataTable.Rows)
                    {
                        if (row["Vault Name"].ToString() == group.Key.VaultName &&
                            row["Type"].ToString().Contains("Acc.") == group.Key.IsAccessory)
                        {
                            row[$"{container} Pos"] = finalPos;
                        }
                    }
                    posCounter++;
                }
            }
            GridBomMatrix.Items.Refresh();
        }

        private void BtnSyncPosToCad_Click(object sender, RoutedEventArgs e)
        {
            if (_lastScanResults == null || _lastScanResults.Count == 0) return;

            Autodesk.AutoCAD.ApplicationServices.Document doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            int updatedCount = 0;
            this.Visibility = Visibility.Hidden;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var blockPosMap = new Dictionary<ObjectId, HashSet<string>>();

                    foreach (var record in _lastScanResults)
                    {
                        if (string.IsNullOrEmpty(record.Position) || record.InstanceHandles == null) continue;
                        foreach (long handleValue in record.InstanceHandles)
                        {
                            Handle h = new Handle(handleValue);
                            if (db.TryGetObjectId(h, out ObjectId objId))
                            {
                                if (!blockPosMap.ContainsKey(objId)) blockPosMap[objId] = new HashSet<string>();
                                blockPosMap[objId].Add(record.Position);
                            }
                        }
                    }

                    foreach (var kvp in blockPosMap)
                    {
                        BlockReference blkRef = tr.GetObject(kvp.Key, OpenMode.ForRead) as BlockReference;
                        if (blkRef?.AttributeCollection == null) continue;

                        string combinedPos = string.Join(",", kvp.Value.OrderBy(x => x));
                        foreach (ObjectId attId in blkRef.AttributeCollection)
                        {
                            AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                            if (attRef != null && attRef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                            {
                                attRef.UpgradeOpen();
                                attRef.TextString = combinedPos;
                                updatedCount++;
                                break;
                            }
                        }
                    }
                    tr.Commit();
                }
                MessageBox.Show($"Successfully synced {updatedCount} Position Tags to CAD!", "Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { this.Visibility = Visibility.Visible; }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_bomDataTable == null || _bomDataTable.Rows.Count == 0) return;
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = (_mode == BomMode.Hull ? "Hull" : "Equipment") + "_BOM_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx"
            };

            if (sfd.ShowDialog() != true) return;

            try
            {
                bool isHull = _mode == BomMode.Hull;

                dynamic excelApp;
                try { excelApp = System.Runtime.InteropServices.Marshal.GetActiveObject("Excel.Application"); }
                catch { excelApp = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application")); }

                excelApp.Visible = false;
                excelApp.DisplayAlerts = false;
                dynamic workbook = excelApp.Workbooks.Add();

                // Sheet 1 — BOM Matrix
                dynamic wsMatrix = workbook.Sheets[1];
                wsMatrix.Name = isHull ? "FittingInHull" : "FittingInEquipment";

                for (int i = 0; i < _bomDataTable.Columns.Count; i++)
                    wsMatrix.Cells[1, i + 1] = _bomDataTable.Columns[i].ColumnName;

                for (int r = 0; r < _bomDataTable.Rows.Count; r++)
                {
                    for (int c = 0; c < _bomDataTable.Columns.Count; c++)
                    {
                        var val = _bomDataTable.Rows[r][c];
                        if (val != System.DBNull.Value) wsMatrix.Cells[r + 2, c + 1] = val.ToString();
                        if (_bomDataTable.Columns[c].ColumnName.EndsWith(" Pos"))
                            wsMatrix.Cells[r + 2, c + 1].NumberFormat = "@";
                    }
                }
                wsMatrix.Columns.AutoFit();

                // Sheet 2 — Detail data
                dynamic wsData = workbook.Sheets.Add(After: workbook.Sheets[1]);
                wsData.Name = isHull ? "Data" : "Part BOM";
                string[] headers = { isHull ? "Hull Name" : "Equipment Name", "Vault Name", "Part ID", "XClass", "Description", "Quantity", "UoM", "Position" };
                for (int i = 0; i < headers.Length; i++) wsData.Cells[1, i + 1] = headers[i];

                var sorted = _lastScanResults
                    .OrderBy(r => r.PanelName)
                    .ThenBy(r => r.IsAccessory ? r.ParentPartId : r.VaultName)
                    .ThenBy(r => r.IsAccessory)
                    .ToList();

                for (int r = 0; r < sorted.Count; r++)
                {
                    var item = sorted[r];
                    wsData.Cells[r + 2, 1] = item.PanelName;
                    wsData.Cells[r + 2, 2] = item.VaultName;
                    wsData.Cells[r + 2, 3] = item.PartId;
                    wsData.Cells[r + 2, 4] = item.XClass;
                    wsData.Cells[r + 2, 5] = item.Description;
                    wsData.Cells[r + 2, 6] = item.Quantity;
                    wsData.Cells[r + 2, 7] = item.UoM;
                    wsData.Cells[r + 2, 8].NumberFormat = "@";
                    wsData.Cells[r + 2, 8] = item.Position;
                    if (item.IsAccessory) wsData.Cells[r + 2, 5].Font.Italic = true;
                }
                wsData.Columns.AutoFit();

                workbook.SaveAs(sfd.FileName);
                workbook.Close();
                excelApp.Quit();

                System.Runtime.InteropServices.Marshal.ReleaseComObject(wsData);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(wsMatrix);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);

                MessageBox.Show("BOM Exported successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Export Error: " + ex.Message); }
        }
    }
}
