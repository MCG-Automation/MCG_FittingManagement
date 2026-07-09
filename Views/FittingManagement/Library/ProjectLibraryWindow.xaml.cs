using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Window quản lý Project Library — file JSON gắn với 1 project cụ thể.
    /// Workflow: load/create project, edit pos number, auto-assign pos, insert vào CAD.
    /// Khi có project được load → set <see cref="ActiveProjectContext"/> để Master window biết.
    /// </summary>
    public partial class ProjectLibraryWindow : Window
    {
        private const string LOG_PREFIX = "[ProjectLibraryWindow]";

        private readonly IProjectLibraryService _projectService;
        private readonly IFittingManagementService _fittingService;
        private readonly IMasterLibraryService _masterService;
        private readonly IFittingPreviewService _previewService;
        private readonly ActiveProjectContext _projectContext;
        private List<ProjectCatalogItem> _fullCatalog = new List<ProjectCatalogItem>();
        private IRecentItemsTracker _recentTracker;

        public ProjectLibraryWindow(IProjectLibraryService projectService,
                                    IFittingManagementService fittingService,
                                    IMasterLibraryService masterService)
        {
            InitializeComponent();
            _projectService = projectService;
            _fittingService = fittingService;
            _masterService = masterService;
            _previewService = new FittingPreviewService();
            _projectContext = ActiveProjectContext.Instance;

            PreviewPane.Initialize(_previewService);
            GridCatalog.SelectionChanged += GridCatalog_SelectionChanged;
            this.Closed += (_, __) => _previewService.ClearAllCache();

            // Nếu đã có project active từ trước → tự load
            if (_projectContext.HasActiveProject)
            {
                TxtCurrentProject.Text = _projectContext.ProjectDisplayName;
                LoadCatalog();
            }
        }

        private void GridCatalog_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var first = GridCatalog.SelectedItems.Cast<CatalogItem>().FirstOrDefault();
            PreviewPane.ShowItem(first);
        }

        private void LoadCatalog()
        {
            string path = _projectContext.ProjectFilePath;
            Debug.WriteLine($"{LOG_PREFIX} LoadCatalog từ {path}");
            try
            {
                _fullCatalog = string.IsNullOrEmpty(path)
                    ? new List<ProjectCatalogItem>()
                    : _projectService.LoadProjectCatalog(path);

                // Recently store: cùng folder với project JSON, đặt tên theo project
                _recentTracker = string.IsNullOrEmpty(path)
                    ? null
                    : new RecentItemsTracker(GetRecentStorePath(path));

                BuildCategoryTree();
                ApplyFilters();
                PreviewPane.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot load project: " + ex.Message);
                _fullCatalog = new List<ProjectCatalogItem>();
            }
        }

        /// <summary>Đường dẫn file Recently store cạnh project JSON: <c>&lt;name&gt;.recent.json</c>.</summary>
        private static string GetRecentStorePath(string projectJsonPath)
        {
            string folder = Path.GetDirectoryName(projectJsonPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(projectJsonPath);
            return Path.Combine(folder, name + ".recent.json");
        }

        private void BuildCategoryTree()
        {
            // Cast lên CatalogItem để dùng builder chung
            var asBase = _fullCatalog.Cast<CatalogItem>().ToList();
            var recent = _recentTracker?.GetRecentBlockNames();
            TreeCategories.ItemsSource = CatalogTreeBuilder.Build(asBase, recent);
        }

        private void TreeCategories_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => ApplyFilters();
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (_fullCatalog == null) return;
            IEnumerable<CatalogItem> source = _fullCatalog.Cast<CatalogItem>();
            if (TreeCategories.SelectedItem is CategoryNode node && node.CategoryName != "All Fittings")
                source = node.Items;
            GridCatalog.ItemsSource = CatalogTreeBuilder.ApplySearch(source, TxtSearch.Text ?? "");
        }

        // =========================================================
        // Load / Create project
        // =========================================================
        private void BtnLoadProject_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Title = "Select Item Library", Filter = "JSON Files (*.json)|*.json" };
            if (ofd.ShowDialog() != true) return;

            _projectContext.ProjectFilePath = ofd.FileName;
            TxtCurrentProject.Text = _projectContext.ProjectDisplayName;
            LoadCatalog();
        }

        private void BtnCreateProject_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Create New Item Library",
                Filter = "JSON Files (*.json)|*.json",
                FileName = "New_Project_Catalog.json"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                _projectService.CreateProjectCatalog(sfd.FileName);
                _projectContext.ProjectFilePath = sfd.FileName;
                TxtCurrentProject.Text = _projectContext.ProjectDisplayName;
                LoadCatalog();
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

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                foreach (var item in selected)
                {
                    if (item.EntityType != "Block")
                    {
                        MessageBox.Show($"Cannot insert {item.EntityType} as Block.");
                        continue;
                    }
                    _fittingService.InsertBlockFromLibrary(item.FilePath, item.BlockName);
                    _recentTracker?.Track(item.BlockName);
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // =========================================================
        // Refresh — reload từ file JSON + đồng bộ property mới nhất từ Master Library
        // (nếu fitting đã được cập nhật Title/Description/Mass/... bên Master)
        // =========================================================
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_projectContext.HasActiveProject)
            {
                try
                {
                    int updated = RefreshPropertiesFromMaster();
                    Debug.WriteLine($"{LOG_PREFIX} Refresh: {updated} item(s) đồng bộ property từ Master Library.");
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            LoadCatalog();
        }

        /// <summary>
        /// So khớp item trong project catalog với Master Catalog theo BlockName.
        /// Nếu metadata bên Master khác với project (đã được Edit Properties / Sync ở Master),
        /// cập nhật xuống project catalog — giữ nguyên ProjectPosNum. Trả về số item đã cập nhật.
        /// </summary>
        private int RefreshPropertiesFromMaster()
        {
            if (_masterService == null) return 0;

            string path = _projectContext.ProjectFilePath;
            var projectCatalog = _projectService.LoadProjectCatalog(path);
            if (projectCatalog.Count == 0) return 0;

            var masterItems = _masterService.GetMasterCatalogItems()
                .Where(m => !string.IsNullOrEmpty(m.BlockName))
                .ToDictionary(m => m.BlockName, m => m, StringComparer.Ordinal);

            int updatedCount = 0;
            foreach (var item in projectCatalog)
            {
                if (string.IsNullOrEmpty(item.BlockName)) continue;
                if (!masterItems.TryGetValue(item.BlockName, out CatalogItem master)) continue;

                bool changed = item.PartNumber != master.PartNumber
                    || item.Title != master.Title
                    || item.Description != master.Description
                    || item.Material != master.Material
                    || item.Mass != master.Mass
                    || item.Revision != master.Revision
                    || item.Designer != master.Designer
                    || item.BomType != master.BomType
                    || item.UoM != master.UoM;
                if (!changed) continue;

                item.PartNumber  = master.PartNumber;
                item.Title       = master.Title;
                item.Description = master.Description;
                item.Material    = master.Material;
                item.Mass        = master.Mass;
                item.Revision    = master.Revision;
                item.Designer    = master.Designer;
                item.BomType     = master.BomType;
                item.UoM         = master.UoM;
                updatedCount++;
            }

            if (updatedCount > 0) _projectService.SaveProjectCatalog(path, projectCatalog);
            return updatedCount;
        }

        // =========================================================
        // Right-click context menu — chọn row trước khi mở menu (giống Windows Explorer)
        // =========================================================
        private void GridRow_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && !row.IsSelected)
            {
                GridCatalog.SelectedItems.Clear();
                row.IsSelected = true;
            }
        }

        // =========================================================
        // Edit Properties — chỉ cho phép sửa Project Pos Num
        // =========================================================
        private void BtnEditProperties_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject) return;

            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select item(s) to edit.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dlg = new EditPosNumDialog(selected) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _projectService.SaveProjectCatalog(_projectContext.ProjectFilePath, _fullCatalog);
                    foreach (var it in selected) _recentTracker?.Track(it.BlockName);
                    LoadCatalog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =========================================================
        // Sync — đồng bộ property của catalog xuống drawing đang mở
        // (chuyển từ Master Library sang Item Library)
        // =========================================================
        private void BtnSyncToDrawing_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select item(s) in the grid first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var blockItems = selected.Where(i => i.EntityType == "Block" && !string.IsNullOrEmpty(i.BlockName)).ToList();
            if (blockItems.Count == 0)
            {
                MessageBox.Show("No Block-type items selected (Sync only applies to Block entries).",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Sync catalog properties for {blockItems.Count} block(s) to the current drawing?\n\n" +
                "Updates both block definitions (AttributeDefinitions) and all existing\n" +
                "INSERT instances (AttributeReferences) in the current drawing.",
                "Confirm Sync to Drawing", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var result = _masterService.SyncPropertiesFromCatalogToDrawing(blockItems);
                ShowPushUpdateResult(result, "Sync to Drawing Result");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowPushUpdateResult(Models.FittingManagement.PushUpdateResult result, string title = "Sync Result")
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
                foreach (var err in result.Errors.Take(8)) sb.AppendLine(err);
                if (result.Errors.Count > 8) sb.AppendLine($"  ... and {result.Errors.Count - 8} more (see log).");
            }

            var icon = result.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
            MessageBox.Show(sb.ToString(), title, MessageBoxButton.OK, icon);
        }

        private void BtnAutoAssignPos_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("Load or create a project first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                int groups = _projectService.AutoAssignPositions(_projectContext.ProjectFilePath);
                LoadCatalog();
                MessageBox.Show($"Auto-assigned {groups} position group(s).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        // =========================================================
        // Remove
        // =========================================================
        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject) return;

            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show($"Remove {selected.Count} item(s) from project?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                _projectService.RemoveFromProject(_projectContext.ProjectFilePath, selected.Select(s => s.BlockName));
                LoadCatalog();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }
    }
}
