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
        private readonly IFittingPreviewService _previewService;
        private readonly ActiveProjectContext _projectContext;
        private List<ProjectCatalogItem> _fullCatalog = new List<ProjectCatalogItem>();
        private IRecentItemsTracker _recentTracker;

        public ProjectLibraryWindow(IProjectLibraryService projectService,
                                    IFittingManagementService fittingService)
        {
            InitializeComponent();
            _projectService = projectService;
            _fittingService = fittingService;
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

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadCatalog();

        // =========================================================
        // Edit pos number / auto-assign
        // =========================================================
        private void GridCatalog_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column != ColProjectPos) return;
            if (!_projectContext.HasActiveProject) return;

            if (e.Row.Item is CatalogItem editedItem)
            {
                string newValue = ((TextBox)e.EditingElement).Text;
                if (editedItem.ProjectPosNum == newValue) return;

                editedItem.ProjectPosNum = newValue;
                try
                {
                    _projectService.SaveProjectCatalog(_projectContext.ProjectFilePath, _fullCatalog);
                    _recentTracker?.Track(editedItem.BlockName);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
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
