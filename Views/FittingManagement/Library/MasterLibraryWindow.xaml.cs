using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Window quản lý Master Library — nguồn template gốc tại MasterCatalog.json.
    /// Workflow: thêm fitting từ CAD, edit accessories, publish block, push sang Active Project.
    /// </summary>
    public partial class MasterLibraryWindow : Window
    {
        private const string LOG_PREFIX = "[MasterLibraryWindow]";

        private readonly IMasterLibraryService _masterService;
        private readonly IProjectLibraryService _projectService;
        private readonly IFittingManagementService _fittingService;
        private readonly IFittingPreviewService _previewService;
        private readonly ActiveProjectContext _projectContext;
        private readonly IRecentItemsTracker _recentTracker;
        private List<CatalogItem> _fullCatalog;

        public MasterLibraryWindow(IMasterLibraryService masterService,
                                   IProjectLibraryService projectService,
                                   IFittingManagementService fittingService)
        {
            InitializeComponent();
            _masterService = masterService;
            _projectService = projectService;
            _fittingService = fittingService;
            _previewService = new FittingPreviewService();
            _projectContext = ActiveProjectContext.Instance;

            // Recently store: cùng folder với MasterCatalog.json
            string recentStorePath = Path.Combine(_masterService.MasterLibraryFolder, "MasterCatalog.recent.json");
            _recentTracker = new RecentItemsTracker(recentStorePath);

            _projectContext.ProjectChanged += OnActiveProjectChanged;
            this.Closed += (_, __) =>
            {
                _projectContext.ProjectChanged -= OnActiveProjectChanged;
                _previewService.ClearAllCache();
            };

            PreviewPane.Initialize(_previewService);
            GridCatalog.SelectionChanged += GridCatalog_SelectionChanged;

            UpdateActiveProjectLabel();
            LoadCatalog();
        }

        private void GridCatalog_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Khi multi-select: hiện preview của item ĐẦU TIÊN selected (representative)
            var first = GridCatalog.SelectedItems.Cast<CatalogItem>().FirstOrDefault();
            PreviewPane.ShowItem(first);
        }

        private void OnActiveProjectChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(UpdateActiveProjectLabel);
        }

        private void UpdateActiveProjectLabel()
        {
            bool hasProject = _projectContext.HasActiveProject;
            TxtActiveProject.Text = hasProject ? _projectContext.ProjectDisplayName : "(none)";
            BtnAddToProject.IsEnabled = hasProject;
            BtnAddToProject.ToolTip = hasProject
                ? $"Add to: {_projectContext.ProjectFilePath}"
                : "Open a Project Library window to set the active project.";
        }

        private void LoadCatalog()
        {
            Debug.WriteLine($"{LOG_PREFIX} LoadCatalog từ {_masterService.MasterCatalogPath}");
            try
            {
                _fullCatalog = _masterService.GetMasterCatalogItems();
                BuildCategoryTree();
                ApplyFilters();
                PreviewPane.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot load Master Library: " + ex.Message);
                _fullCatalog = new List<CatalogItem>();
            }
        }

        private void BuildCategoryTree()
        {
            var recent = _recentTracker?.GetRecentBlockNames();
            TreeCategories.ItemsSource = CatalogTreeBuilder.Build(_fullCatalog, recent);
        }

        private void TreeCategories_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => ApplyFilters();
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (_fullCatalog == null) return;
            IEnumerable<CatalogItem> source = _fullCatalog;
            if (TreeCategories.SelectedItem is CategoryNode node && node.CategoryName != "All Fittings")
                source = node.Items;
            GridCatalog.ItemsSource = CatalogTreeBuilder.ApplySearch(source, TxtSearch.Text ?? "");
        }

        // =========================================================
        // Add from CAD — pick virtual item & save to master
        // =========================================================
        private void BtnAddFromCad_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnAutoCadIdle;
        }

        private void OnAutoCadIdle(object sender, EventArgs e)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnAutoCadIdle;
            try
            {
                var draftItem = _fittingService.PickGeometricFeatureFromCad();
                if (draftItem != null)
                {
                    var virtualWin = new VirtualItemWindow(_masterService, draftItem) { Owner = this };
                    if (virtualWin.ShowDialog() == true)
                {
                    _recentTracker?.Track(draftItem.BlockName);
                    LoadCatalog();
                }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.Show();
            }
        }

        private void BtnManageAccessory_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count != 1) return;
            try
            {
                var accWin = new AccessoryManagerWindow(_masterService, selected[0]) { Owner = this };
                if (accWin.ShowDialog() == true)
                {
                    _recentTracker?.Track(selected[0].BlockName);
                    LoadCatalog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // =========================================================
        // Insert / Refresh
        // =========================================================
        private void BtnInsert_Click(object sender, RoutedEventArgs e) => InsertSelected();
        private void GridCatalog_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => InsertSelected();

        private void InsertSelected()
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0) return;

            var blockItems    = selected.Where(i => i.EntityType == "Block").ToList();
            var nonBlockItems = selected.Where(i => i.EntityType != "Block").ToList();

            foreach (var item in nonBlockItems)
                MessageBox.Show($"Cannot insert '{item.BlockName}' — entity type '{item.EntityType}' is not a Block.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);

            if (blockItems.Count == 0) return;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                if (blockItems.Count == 1)
                {
                    // Single insert: giữ nguyên behavior (prompt riêng từng block)
                    _fittingService.InsertBlockFromLibrary(blockItems[0].FilePath, blockItems[0].BlockName);
                    _recentTracker?.Track(blockItems[0].BlockName);
                }
                else
                {
                    // Multi-insert: 1 prompt duy nhất, trải theo extents
                    _fittingService.InsertMultipleBlocksFromLibrary(blockItems);
                    foreach (var item in blockItems) _recentTracker?.Track(item.BlockName);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadCatalog();

        // =========================================================
        // Add to Active Project
        // =========================================================
        private void BtnAddToProject_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("No active project. Open a Project Library window and load/create one first.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0) return;

            try
            {
                var result = _projectService.MergeIntoProject(_projectContext.ProjectFilePath, selected);
                MessageBox.Show($"Added to project '{_projectContext.ProjectDisplayName}'.\nNew: {result.Item1} | Updated: {result.Item2}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // =========================================================
        // Remove
        // =========================================================
        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show($"Remove {selected.Count} item(s) from Master Library?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                _masterService.RemoveFromMaster(selected.Select(s => s.BlockName));
                LoadCatalog();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // =========================================================
        // Push Update — Wblock block từ drawing đang mở GHI ĐÈ file .dwg trong Master Library.
        // Khác với "Redefine Blocks" (drawing -> drawing khác đang mở): Push Update là drawing -> file .dwg trong Library.
        // =========================================================
        private void BtnUpdateLibrary_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select item(s) in the grid first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Chỉ áp dụng cho item type Block (Accessory/Virtual không có .dwg để ghi đè)
            var blockItems = selected.Where(i => i.EntityType == "Block" && !string.IsNullOrEmpty(i.BlockName) && !string.IsNullOrEmpty(i.FilePath)).ToList();
            if (blockItems.Count == 0)
            {
                MessageBox.Show("No Block-type items selected (Push Update only applies to Block entries).",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Push current drawing definition of {blockItems.Count} block(s) back to Master Library?\n\n" +
                "This will OVERWRITE the .dwg file(s) in the Library folder with the block definition\n" +
                "from the currently open drawing.",
                "Confirm Push Update", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var result = _masterService.PushBlocksFromCurrentDrawing(blockItems);
                // Invalidate preview cache cho tất cả item user vừa Push (file .dwg vừa thay đổi)
                foreach (var it in blockItems) _previewService.InvalidatePreview(it);
                // Đẩy các block update thành công vào danh sách Recently
                if (result?.Updated != null)
                {
                    foreach (var name in result.Updated) _recentTracker?.Track(name);
                }
                ShowPushUpdateResult(result);
                LoadCatalog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowPushUpdateResult(Models.FittingManagement.PushUpdateResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"✓ Updated:  {result.SuccessCount}");
            sb.AppendLine($"⚠ Skipped:  {result.SkippedCount}  (block not in current drawing)");
            sb.AppendLine($"✗ Failed:   {result.FailCount}");

            if (result.NotFoundInDrawing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("── Not found in drawing ──");
                foreach (var n in result.NotFoundInDrawing.Take(8)) sb.AppendLine($"  • {n}");
                if (result.NotFoundInDrawing.Count > 8) sb.AppendLine($"  ... and {result.NotFoundInDrawing.Count - 8} more");
            }

            if (result.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("── Errors ──");
                foreach (var e in result.Errors.Take(8)) sb.AppendLine(e);
                if (result.Errors.Count > 8) sb.AppendLine($"  ... and {result.Errors.Count - 8} more (see log).");
            }

            var icon = result.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
            MessageBox.Show(sb.ToString(), "Push Update Result", MessageBoxButton.OK, icon);
        }
    }
}
