using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Window "Fitting Table" — quản lý catalog của 1 Project Folder (mô hình "1 Project = 1 Folder",
    /// xem <see cref="ActiveProjectContext"/>). Nguồn dữ liệu hiển thị là toàn bộ catalog
    /// (FittingCatalog.json) của Project Folder đang active — chỉ 1 tầng dữ liệu duy nhất, không còn
    /// Master Catalog toàn công ty + overlay Project riêng như thiết kế cũ. Mở từ tab Palette
    /// "Fitting Table" qua <see cref="ShowOrActivate"/> — dùng chung 1 instance.
    /// </summary>
    public partial class FittingTableWindow : Window
    {
        private const string LOG_PREFIX = "[FittingTableWindow]";

        private readonly IMasterLibraryService _masterService;
        private readonly IFittingManagementService _fittingService;
        private readonly IFittingPreviewService _previewService;
        private readonly ActiveProjectContext _projectContext;
        private IRecentItemsTracker _recentTracker;
        private List<CatalogItem> _fullCatalog;

        // =========================================================
        // Customize Grid — cột cố định (11 cột khai báo tĩnh trong XAML) mặc định HIỆN; cột động (từ
        // CatalogItem.ExtraProperties — iProperty/Vault field ngoài 7 field chính) mặc định ẨN. User
        // click phải Header để bật/tắt bất kỳ cột nào, lưu qua GridColumnSettingsStore (dùng chung mọi
        // Project). Key = Header text (string) của cột — dùng chung cho cả cột tĩnh lẫn động.
        // =========================================================
        private static readonly HashSet<string> FixedColumnHeaders = new HashSet<string>
        {
            "Type", "Source", "View / File Name", "Part Complete ID", "Name", "Description",
            "Project Pos.", "UoM", "Weight", "Originator", "Created"
        };
        private Dictionary<string, bool> _columnVisibilityOverrides;

        // Kéo-thả sắp xếp lại Sub Folder (Level 2 — 1 PartNumber) để đổi thứ tự hiển thị (CategorySortOrder).
        private Point? _dragStartPoint;

        // Multi-select Sub Folder (Ctrl/Shift-click) — TreeView chỉ hỗ trợ single-select gốc nên tự
        // quản lý tập chọn riêng (không dùng TreeCategories.SelectedItem cho trường hợp nhiều node).
        private readonly List<CategoryNode> _selectedCategoryNodes = new List<CategoryNode>();
        private CategoryNode _multiSelectAnchor;

        // =========================================================
        // Singleton instance dùng chung giữa tab "Template" và (nếu có) nơi khác gọi
        // =========================================================
        private static FittingTableWindow _instance;

        public static void ShowOrActivate(IMasterLibraryService masterService,
                                          IFittingManagementService fittingService)
        {
            if (_instance != null && _instance.IsLoaded)
            {
                _instance.Activate();
                return;
            }
            _instance = new FittingTableWindow(masterService, fittingService);
            _instance.Closed += (_, __) => _instance = null;
            // Dùng WPF .Show() THAY cho Application.ShowModelessWindow: ShowModelessWindow gán AutoCAD
            // làm owner → cửa sổ (owned) LUÔN nổi trên AutoCAD, click vào bản vẽ không đẩy nó ra sau
            // được (user phải bấm minimize/hide). .Show() (không owner) cho cửa sổ theo z-order chuẩn
            // Windows → user chỉ cần click vào màn hình CAD là cửa sổ tự lùi ra sau (background). Mở lại
            // bằng chính lệnh này (Activate ở nhánh trên) hoặc click nút trên taskbar.
            _instance.Show();
        }

        public FittingTableWindow(IMasterLibraryService masterService,
                                  IFittingManagementService fittingService)
        {
            InitializeComponent();
            _masterService = masterService;
            _fittingService = fittingService;
            _previewService = new FittingPreviewService();
            _projectContext = ActiveProjectContext.Instance;
            _columnVisibilityOverrides = GridColumnSettingsStore.LoadVisibility();
            ApplyColumnVisibility(); // áp override đã lưu (nếu có) cho 11 cột tĩnh khai báo trong XAML

            RefreshRecentTracker();

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

        /// <summary>Recently store nằm TRONG Project Folder đang active — phải tạo lại tracker mỗi khi
        /// folder đổi (không còn cố định như MasterCatalog.recent.json global trước đây).</summary>
        private void RefreshRecentTracker()
        {
            _recentTracker = _projectContext.HasActiveProject
                ? new RecentItemsTracker(Path.Combine(_masterService.MasterLibraryFolder, "RecentItems.json"))
                : null;
        }

        private void GridCatalog_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Khi multi-select: hiện preview của item ĐẦU TIÊN selected (representative)
            var first = GridCatalog.SelectedItems.Cast<CatalogItem>().FirstOrDefault();
            PreviewPane.ShowItem(first);
        }

        private void OnActiveProjectChanged(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshRecentTracker();
                UpdateActiveProjectLabel();
                LoadCatalog();
            });
        }

        private void UpdateActiveProjectLabel()
        {
            bool hasProject = _projectContext.HasActiveProject;
            TxtActiveProject.Text = hasProject ? _projectContext.ProjectDisplayName : "(none)";
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
        // Right-click Category — TÁI SỬ DỤNG y hệt 5 thao tác của Grid (Edit Properties/Pos Num/View
        // Type/Sync/Remove) nhưng áp dụng cho TOÀN BỘ fitting trong (các) category đang chọn thay vì
        // chỉ item đang chọn trên Grid. Hỗ trợ multi-select (Ctrl/Shift-click, xem
        // TreeViewItem_PreviewMouseLeftButtonDown) — right-click ngoài vùng đang multi-select sẽ
        // collapse về đúng 1 node (giống Windows Explorer); right-click TRONG vùng đang multi-select
        // giữ nguyên cả nhóm. Không cần viết lại business logic: ApplyFilters() lọc Grid theo union
        // item của (các) node đang chọn → SelectAll() trên Grid → gọi thẳng handler Btn*_Click hiện có
        // (chúng chỉ đọc GridCatalog.SelectedItems).
        // =========================================================
        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(sender is TreeViewItem item)) return;

            // EventSetter áp cho MỌI TreeViewItem (mọi cấp) — event TUNNEL qua node cha trước khi tới
            // node con dưới chuột. Chỉ xử lý ở ĐÚNG node dưới chuột (không phải node cha lồng ngoài),
            // tránh chọn nhầm category cha khi right-click 1 node con.
            var actualItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
            if (actualItem != item) return;
            if (!(item.DataContext is CategoryNode node)) return;
            e.Handled = true;

            bool alreadyInMultiSelection = node.Level == 2 && _selectedCategoryNodes.Count > 1 && _selectedCategoryNodes.Contains(node);
            if (!alreadyInMultiSelection)
            {
                if (node.Level == 2) SetSingleCategorySelection(node);
                else ClearCategorySelection();
                item.IsSelected = true; // trigger TreeCategories_SelectedItemChanged (đồng bộ) cho Level 0/1
                item.Focus();
                _multiSelectAnchor = node.Level == 2 ? node : null;
            }

            ApplyFilters();
            GridCatalog.SelectAll(); // chọn TOÀN BỘ fitting đang hiện trên Grid (union của các node đang chọn)

            int itemCount = GridCatalog.SelectedItems.Count;
            // Level 2 (Sub Folder) = ĐÚNG 1 fitting, bên trong là các VIEW (không phải "fitting(s)") —
            // Level 0/1 vẫn là category tổng hợp NHIỀU fitting thật nên giữ nguyên "fitting(s)".
            string label;
            if (_selectedCategoryNodes.Count > 1)
                label = $"{_selectedCategoryNodes.Count} Sub Folders — {itemCount} view(s) total";
            else if (node.Level == 2)
                label = $"{node.CategoryName} — {itemCount} view(s)";
            else
                label = $"{node.CategoryName} — {itemCount} fitting(s)";

            var menu = BuildCategoryContextMenu(label);
            item.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private static TreeViewItem FindAncestorTreeViewItem(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            return source as TreeViewItem;
        }

        /// <summary>Menu right-click Category — dựng động (giống BuildCustomizeGridMenu), header hiện
        /// đúng <paramref name="label"/> đã format sẵn (xem TreeViewItem_PreviewMouseRightButtonDown —
        /// wording "fitting(s)" vs "view(s)" tuỳ Level). Mỗi MenuItem gọi THẲNG handler y hệt Grid —
        /// không trùng lặp business logic.</summary>
        private ContextMenu BuildCategoryContextMenu(string label)
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = label, IsEnabled = false, FontWeight = FontWeights.Bold });
            menu.Items.Add(new Separator());

            var editProps = new MenuItem { Header = "Edit Properties" };
            editProps.Click += BtnEditProperties_Click;
            menu.Items.Add(editProps);

            var editPos = new MenuItem { Header = "Edit Pos Num", ToolTip = "Sửa Project Pos Num của mọi fitting trong category này." };
            editPos.Click += BtnEditPosNum_Click;
            menu.Items.Add(editPos);

            var editView = new MenuItem { Header = "Edit View Type", ToolTip = "Đánh dấu Plan View / quy tắc đếm Qty theo Plan View cho Hull BOM khi 1 fitting có nhiều mặt cắt trong cùng 1 khung A1." };
            editView.Click += BtnEditViewType_Click;
            menu.Items.Add(editView);

            var sync = new MenuItem { Header = "Sync", ToolTip = "Update block AttributeDefinitions and all INSERT instances in the current drawing." };
            sync.Click += BtnSyncToDrawing_Click;
            menu.Items.Add(sync);

            var accessory = new MenuItem { Header = "Sub-BOM / Accessories", ToolTip = "Manage accessories attached to this fitting." };
            accessory.Click += BtnManageAccessory_Click;
            menu.Items.Add(accessory);

            var copyTo = new MenuItem { Header = "Copy to...", ToolTip = "Copy fitting(s) — block .dwg + accessories — sang 1 Project Folder khác và cập nhật FittingCatalog.json của dự án đó." };
            copyTo.Click += BtnCopyTo_Click;
            menu.Items.Add(copyTo);

            menu.Items.Add(new Separator());

            var remove = new MenuItem { Header = "Remove from Project", Foreground = System.Windows.Media.Brushes.Firebrick };
            remove.Click += BtnRemoveFromMaster_Click;
            menu.Items.Add(remove);

            return menu;
        }

        // =========================================================
        // Multi-select Sub Folder (Ctrl/Shift-click) + kéo-thả sắp xếp lại (Level 2 — mỗi Sub Folder =
        // ĐÚNG 1 PartNumber) → đổi CategorySortOrder (thứ tự HIỂN THỊ, KHÔNG PHẢI ProjectPosNum thật —
        // xem CatalogItem.CategorySortOrder). Chỉ cho kéo-thả/range-select trong CÙNG 1 BomType
        // category (không cho chéo Equipment/Hull).
        // =========================================================
        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var actualItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
            if (!(sender is TreeViewItem item) || actualItem != item) return;
            if (!(item.DataContext is CategoryNode node)) { _dragStartPoint = null; return; }

            bool ctrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
            bool shift = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);

            if (node.Level == 2 && (ctrl || shift))
            {
                ToggleOrRangeSelectCategoryNode(node, shift);
                ApplyFilters();
                e.Handled = true; // chặn native single-select (mất multi-select) + không khởi động kéo-thả
                _dragStartPoint = null;
                return;
            }

            // Plain click (không giữ Ctrl/Shift): để native TreeView tự đổi selection như bình thường —
            // TreeCategories_SelectedItemChanged (đã có sẵn) sẽ đồng bộ lại _selectedCategoryNodes, tránh
            // xung đột timing giữa Preview/tunnel (chạy TRƯỚC khi native selection cập nhật) và bubble.
            // Chỉ ghi lại điểm bắt đầu kéo (nếu Level 2) để hỗ trợ kéo-thả reorder.
            _dragStartPoint = node.Level == 2 ? (Point?)e.GetPosition(null) : null;
        }

        private void ToggleOrRangeSelectCategoryNode(CategoryNode node, bool rangeSelect)
        {
            if (rangeSelect && _multiSelectAnchor != null && node.Parent != null && _multiSelectAnchor.Parent == node.Parent)
            {
                var siblings = node.Parent.Children;
                int i1 = siblings.IndexOf(_multiSelectAnchor);
                int i2 = siblings.IndexOf(node);
                if (i1 >= 0 && i2 >= 0)
                {
                    ClearCategorySelection();
                    int lo = Math.Min(i1, i2), hi = Math.Max(i1, i2);
                    for (int k = lo; k <= hi; k++)
                    {
                        _selectedCategoryNodes.Add(siblings[k]);
                        siblings[k].IsMultiSelected = true;
                    }
                    return;
                }
            }

            // Ctrl toggle (hoặc Shift không có anchor hợp lệ trong cùng parent -> fallback toggle đơn)
            if (_selectedCategoryNodes.Contains(node))
            {
                _selectedCategoryNodes.Remove(node);
                node.IsMultiSelected = false;
            }
            else
            {
                _selectedCategoryNodes.Add(node);
                node.IsMultiSelected = true;
                _multiSelectAnchor = node;
            }
        }

        private void SetSingleCategorySelection(CategoryNode node)
        {
            ClearCategorySelection();
            _selectedCategoryNodes.Add(node);
            node.IsMultiSelected = true;
        }

        private void ClearCategorySelection()
        {
            foreach (var n in _selectedCategoryNodes) n.IsMultiSelected = false;
            _selectedCategoryNodes.Clear();
        }

        private void TreeViewItem_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragStartPoint == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            if (!(sender is TreeViewItem item)) return;

            Point pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            if (!(item.DataContext is CategoryNode node) || node.Level != 2) return;

            _dragStartPoint = null;
            DragDrop.DoDragDrop(item, node, DragDropEffects.Move);
        }

        private void TreeViewItem_Drop(object sender, DragEventArgs e)
        {
            var actualItem = FindAncestorTreeViewItem(e.OriginalSource as DependencyObject);
            if (!(sender is TreeViewItem item) || actualItem != item) return;
            e.Handled = true;

            if (!e.Data.GetDataPresent(typeof(CategoryNode))) return;
            if (!(e.Data.GetData(typeof(CategoryNode)) is CategoryNode dragged)) return;
            if (!(item.DataContext is CategoryNode target)) return;

            if (dragged.Level != 2 || target.Level != 2) return;
            if (dragged.Parent == null || dragged.Parent != target.Parent) return; // chỉ reorder trong CÙNG BomType category
            if (ReferenceEquals(dragged, target)) return;

            var siblings = dragged.Parent.Children;
            int oldIndex = siblings.IndexOf(dragged);
            int newIndex = siblings.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;

            siblings.RemoveAt(oldIndex);
            siblings.Insert(newIndex, dragged);

            RenumberCategorySortOrder(dragged.Parent);
        }

        /// <summary>Gán <see cref="CatalogItem.CategorySortOrder"/> tuần tự (1, 2, 3...) cho MỌI item
        /// trong từng Sub Folder của <paramref name="parent"/>, theo ĐÚNG thứ tự hiện tại của
        /// <c>parent.Children</c> (vừa được kéo-thả sắp xếp lại). CHỈ ghi field thứ tự HIỂN THỊ này —
        /// KHÔNG đụng <see cref="CatalogItem.ProjectPosNum"/> (Pos Num nghiệp vụ thật của BOM/bản vẽ,
        /// đặc biệt Equipment có thể có nhiều ProjectPosNum khác nhau cho cùng 1 PartNumber). Ghi qua
        /// MergeIntoMaster rồi LoadCatalog() refresh — tree build lại từ dữ liệu vừa ghi, tự sort đúng.</summary>
        private void RenumberCategorySortOrder(CategoryNode parent)
        {
            try
            {
                var allItems = new List<CatalogItem>();
                int order = 1;
                foreach (var child in parent.Children)
                {
                    foreach (var it in child.Items)
                    {
                        it.CategorySortOrder = order;
                        allItems.Add(it);
                    }
                    order++;
                }

                if (allItems.Count == 0) return;
                _masterService.MergeIntoMaster(allItems);
                LoadCatalog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadCatalog()
        {
            Debug.WriteLine($"{LOG_PREFIX} LoadCatalog từ {(_projectContext.HasActiveProject ? _masterService.MasterCatalogPath : "<no active project>")}");
            try
            {
                _fullCatalog = _masterService.GetMasterCatalogItems();
                ClearCategorySelection();
                _multiSelectAnchor = null;
                RebuildDynamicColumns();
                BuildCategoryTree();
                ApplyFilters();
                PreviewPane.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot load Fitting Table: " + ex.Message);
                _fullCatalog = new List<CatalogItem>();
            }
        }

        private void BuildCategoryTree()
        {
            var recent = _recentTracker?.GetRecentBlockNames();
            TreeCategories.ItemsSource = CatalogTreeBuilder.Build(_fullCatalog, recent);
        }

        /// <summary>Fire khi native TreeView đổi SelectedItem (plain click, phím mũi tên...). Đồng bộ
        /// lại _selectedCategoryNodes: Level 2 -> chọn đúng 1 node này (reset multi-select cũ); Level
        /// 0/1 hoặc null -> rỗng, ApplyFilters() fallback dùng TreeCategories.SelectedItem như cũ.</summary>
        private void TreeCategories_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is CategoryNode node && node.Level == 2)
            {
                SetSingleCategorySelection(node);
                _multiSelectAnchor = node;
            }
            else
            {
                ClearCategorySelection();
            }
            ApplyFilters();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ApplyFilters()
        {
            if (_fullCatalog == null) return;
            IEnumerable<CatalogItem> source;
            if (_selectedCategoryNodes.Count > 0)
            {
                source = _selectedCategoryNodes.SelectMany(n => n.Items).Distinct();
            }
            else
            {
                source = _fullCatalog;
                if (TreeCategories.SelectedItem is CategoryNode node && node.CategoryName != "All Fittings")
                    source = node.Items;
            }
            GridCatalog.ItemsSource = CatalogTreeBuilder.ApplySearch(source, TxtSearch.Text ?? "");
        }

        // =========================================================
        // Customize Grid — cột động (từ CatalogItem.ExtraProperties) + right-click Header để bật/tắt
        // BẤT KỲ cột nào (tĩnh lẫn động). Xem field FixedColumnHeaders/_columnVisibilityOverrides.
        // =========================================================

        /// <summary>Union mọi key trong ExtraProperties của toàn bộ _fullCatalog → thêm cột MỚI (chưa
        /// từng có) vào GridCatalog. Cột đã tồn tại từ lần load trước GIỮ NGUYÊN (không tạo trùng, tránh
        /// xáo trộn thứ tự/độ rộng mỗi lần reload catalog).</summary>
        private void RebuildDynamicColumns()
        {
            if (_fullCatalog == null) return;

            var existingHeaders = new HashSet<string>(GridCatalog.Columns.Select(c => c.Header?.ToString() ?? ""));
            var keys = _fullCatalog
                .Where(i => i?.ExtraProperties != null)
                .SelectMany(i => i.ExtraProperties.Keys)
                .Distinct()
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            foreach (var key in keys)
            {
                if (existingHeaders.Contains(key)) continue;

                var column = new DataGridTextColumn
                {
                    Header = key,
                    Binding = new System.Windows.Data.Binding($"ExtraProperties[{key}]"),
                    Width = DataGridLength.Auto,
                    Visibility = ResolveVisibility(key)
                };
                GridCatalog.Columns.Add(column);
                existingHeaders.Add(key);
            }
        }

        /// <summary>Áp `_columnVisibilityOverrides` (nếu có) lên MỌI cột hiện có trong GridCatalog (tĩnh
        /// lẫn động) — gọi sau khi load settings (constructor) và mỗi khi có cột mới (RebuildDynamicColumns).</summary>
        private void ApplyColumnVisibility()
        {
            foreach (var column in GridCatalog.Columns)
            {
                string header = column.Header?.ToString() ?? "";
                column.Visibility = ResolveVisibility(header);
            }
        }

        /// <summary>Có override đã lưu → dùng override; không có → cột tĩnh mặc định HIỆN, cột động
        /// (iProperty/Vault field) mặc định ẨN (theo xác nhận của user).</summary>
        private Visibility ResolveVisibility(string columnHeader)
        {
            if (_columnVisibilityOverrides != null && _columnVisibilityOverrides.TryGetValue(columnHeader, out bool visible))
                return visible ? Visibility.Visible : Visibility.Collapsed;
            return FixedColumnHeaders.Contains(columnHeader) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ColumnHeader_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement header)) return;
            header.ContextMenu = BuildCustomizeGridMenu();
            header.ContextMenu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>Build lại MỚI mỗi lần mở (phản ánh đúng danh sách cột hiện có) — 1 MenuItem
        /// checkable cho MỖI cột (tĩnh lẫn động) trong GridCatalog.Columns.</summary>
        private ContextMenu BuildCustomizeGridMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "Customize Grid", IsEnabled = false, FontWeight = FontWeights.Bold });
            menu.Items.Add(new Separator());

            foreach (var column in GridCatalog.Columns)
            {
                string header = column.Header?.ToString() ?? "";
                if (string.IsNullOrEmpty(header)) continue;

                var item = new MenuItem
                {
                    Header = header,
                    IsCheckable = true,
                    IsChecked = column.Visibility == Visibility.Visible
                };
                item.Click += (s, args) =>
                {
                    bool newState = item.IsChecked;
                    column.Visibility = newState ? Visibility.Visible : Visibility.Collapsed;
                    _columnVisibilityOverrides[header] = newState;
                    GridColumnSettingsStore.SaveVisibility(_columnVisibilityOverrides);
                };
                menu.Items.Add(item);
            }
            return menu;
        }

        // =========================================================
        // Load Folder — chọn Project Folder; tự tìm/tạo FittingCatalog.json bên trong (không cần nút
        // "Create Project" riêng — file catalog tự sinh lần đầu có thao tác ghi, GetMasterCatalogItems
        // đã tự trả rỗng nếu file chưa tồn tại).
        // =========================================================
        private void BtnLoadFolder_Click(object sender, RoutedEventArgs e)
        {
            string chosen = PromptSelectProjectFolder();
            if (chosen == null) return;

            _projectContext.ProjectFolderPath = chosen;
            // Setter chỉ bắn ProjectChanged nếu path thật sự đổi — gọi LoadCatalog() luôn để chọn lại
            // đúng folder đang active cũng hoạt động như 1 thao tác refresh.
            RefreshRecentTracker();
            UpdateActiveProjectLabel();
            LoadCatalog();
        }

        /// <summary>Hiện FolderBrowserDialog chọn Project Folder — dùng chung cho Load Folder ở đây và
        /// bước hỏi folder lần đầu khi Import IDW (xem <see cref="TemplateView"/>). Trả về null nếu user hủy.</summary>
        public static string PromptSelectProjectFolder()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Project Folder (chứa FittingCatalog.json + block .dwg của project này)",
                ShowNewFolderButton = true
            })
            {
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
            }
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

        // =========================================================
        // Add from Inventor — import fitting từ file .idw vào Project Folder đang mở. Trước đây nằm ở
        // tab Palette; nay gộp vào cửa sổ cạnh "Add from CAD". KHÔNG tự hỏi/tạo Project Folder — yêu
        // cầu Load Folder trước (khác behavior cũ ở tab). Chọn Equipment/Hull qua ImportBomTypeDialog.
        // =========================================================
        private async void BtnAddFromInventor_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("Load a folder first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var typeDlg = new ImportBomTypeDialog { Owner = this };
            if (typeDlg.ShowDialog() != true) return;
            string bomType = typeDlg.BomType; // "EQUIPMENT" | "HULL"

            var ofd = new OpenFileDialog
            {
                Title = "Select Inventor Drawing Files (.idw)",
                Filter = "Inventor Drawing (*.idw)|*.idw",
                Multiselect = true
            };
            if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;

            const bool pullFromVault = true;
            string originalTitle = this.Title;
            BtnAddFromInventor.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.AppStarting;
            var progress = new Progress<string>(msg => this.Title = $"MacGregor Fitting Table — {msg}");

            try
            {
                ImportResult result = await _fittingService.ImportIdwFilesAsync(ofd.FileNames, bomType, pullFromVault, progress);
                ShowImportResultDialog("Import IDW", result);
                LoadCatalog();
            }
            catch (Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "BtnAddFromInventor_Click", ex);
                MessageBox.Show($"Import error: {ex.Message}\n\nSee log: {FileLogger.LogPath}",
                    "Import IDW Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnAddFromInventor.IsEnabled = true;
                this.Title = originalTitle;
            }
        }

        /// <summary>Dialog kết quả import (mirror phiên bản cũ ở TemplateView) — kèm breakdown Vault +
        /// chi tiết lỗi từng file, nút Yes mở thư mục log.</summary>
        private void ShowImportResultDialog(string title, ImportResult result)
        {
            string message = $"{title} complete.\n\n" +
                             $"✓ Success: {result.SuccessCount}\n" +
                             $"✗ Failed:  {result.FailCount}";

            string vaultSection = BuildVaultBreakdown(result);
            if (!string.IsNullOrEmpty(vaultSection))
                message += "\n\n" + vaultSection;

            if (result.FailCount > 0 && result.Errors.Count > 0)
            {
                int maxErrorsToShow = 8;
                var errorsToShow = result.Errors.Take(maxErrorsToShow).ToList();
                message += "\n\n── Error details ──\n" + string.Join("\n", errorsToShow);

                if (result.Errors.Count > maxErrorsToShow)
                    message += $"\n\n... and {result.Errors.Count - maxErrorsToShow} more (see log).";

                message += $"\n\n── Log ──\n{FileLogger.LogPath}" +
                           "\n\nClick Yes to open the log folder, No to close.";

                var answer = MessageBox.Show(message, $"{title} Result",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes) OpenLogFolder();
            }
            else
            {
                MessageBox.Show(message, $"{title} Result",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>Build breakdown trạng thái Vault cho dialog kết quả import (group theo Status).</summary>
        private string BuildVaultBreakdown(ImportResult result)
        {
            if (result.VaultResults == null || result.VaultResults.Count == 0)
                return null;

            var successGroup = result.VaultResults.Where(r => r.IsSuccess).ToList();
            var skipNotInVault = result.VaultResults.Where(r => r.Status == VaultRefreshStatus.SkippedNotInVault).ToList();
            var skipNoAddIn = result.VaultResults.Where(r => r.Status == VaultRefreshStatus.SkippedNoAddIn).ToList();
            var skipNotLoggedIn = result.VaultResults.Where(r => r.Status == VaultRefreshStatus.SkippedNotLoggedIn).ToList();
            var failedGroup = result.VaultResults.Where(r => r.Status == VaultRefreshStatus.Failed).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("── Vault status ──");

            if (successGroup.Count > 0)
            {
                sb.AppendLine($"✓ Updated to latest: {successGroup.Count} file(s)");
                foreach (var r in successGroup.Take(5)) sb.AppendLine($"  • {Path.GetFileName(r.FilePath)}");
                if (successGroup.Count > 5) sb.AppendLine($"  ... and {successGroup.Count - 5} more");
            }
            if (skipNotInVault.Count > 0)
            {
                sb.AppendLine($"⚠ Not in Vault: {skipNotInVault.Count} file(s) — used local copy");
                foreach (var r in skipNotInVault.Take(3)) sb.AppendLine($"  • {Path.GetFileName(r.FilePath)}");
                if (skipNotInVault.Count > 3) sb.AppendLine($"  ... and {skipNotInVault.Count - 3} more");
            }
            if (skipNoAddIn.Count > 0)
            {
                sb.AppendLine($"⚠ Vault SDK not available: {skipNoAddIn.Count} file(s) — used local copy");
                sb.AppendLine($"  → Install Autodesk Vault Client to enable Vault refresh.");
            }
            if (skipNotLoggedIn.Count > 0)
            {
                sb.AppendLine($"⚠ Not signed in to Vault: {skipNotLoggedIn.Count} file(s) — used local copy");
                sb.AppendLine($"  → Open \"Autodesk Vault\" (Vault Explorer), sign in, then try again.");
            }
            if (failedGroup.Count > 0)
            {
                sb.AppendLine($"✗ Vault error: {failedGroup.Count} file(s) — used local copy");
                foreach (var r in failedGroup.Take(3)) sb.AppendLine($"  • {Path.GetFileName(r.FilePath)}: {r.Message}");
                if (failedGroup.Count > 3) sb.AppendLine($"  ... and {failedGroup.Count - 3} more (see log).");
            }

            return sb.ToString().TrimEnd();
        }

        private void OpenLogFolder()
        {
            try
            {
                if (File.Exists(FileLogger.LogPath))
                    Process.Start("explorer.exe", $"/select,\"{FileLogger.LogPath}\"");
                else
                    Process.Start("explorer.exe", FileLogger.LogDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Cannot open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // Copy to... — copy fitting(s) đang chọn (block .dwg + Accessories nhúng trong CatalogItem) sang
        // 1 Project Folder KHÁC, cập nhật FittingCatalog.json của dự án đó. Không đổi Project đang active.
        // =========================================================
        private void BtnCopyTo_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select item(s) to copy.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string targetFolder = PromptSelectProjectFolder();
            if (targetFolder == null) return;

            // Không cho copy vào chính Project Folder đang mở.
            if (_projectContext.HasActiveProject &&
                string.Equals(Path.GetFullPath(targetFolder).TrimEnd('\\'),
                              Path.GetFullPath(_masterService.MasterLibraryFolder).TrimEnd('\\'),
                              StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Target folder is the same as the current Project Folder. Choose a different one.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var res = _masterService.CopyItemsToProjectFolder(selected, targetFolder);
                MessageBox.Show(
                    $"Copied {res.Item1} item(s) to:\n{targetFolder}\n\n" +
                    $"Block .dwg files copied: {res.Item2}\n" +
                    "Accessories were copied together with each fitting.\n" +
                    "The target project's FittingCatalog.json has been updated.",
                    "Copy to", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Sub-BOM / Accessories — dùng chung cho Grid row context menu VÀ Category Sub
        /// Folder/Views context menu. Khi nhiều item đang chọn (multi-view của cùng 1 fitting, hoặc
        /// union nhiều Sub Folder), dùng item ĐẦU TIÊN làm representative (mirror pattern
        /// <c>rep = rows[r][0]</c> của InsertFittingTable cho trường hợp multi-view/fitting).</summary>
        private void BtnManageAccessory_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            var rep = selected.FirstOrDefault();
            if (rep == null)
            {
                MessageBox.Show("Select item(s) first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var accWin = new AccessoryManagerWindow(_masterService, rep) { Owner = this };
                if (accWin.ShowDialog() == true)
                {
                    _recentTracker?.Track(rep.BlockName);
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

        // =========================================================
        // Insert Fitting Table — chèn bảng lưới TOÀN BỘ fitting trong Project (hoặc trong Category
        // đang chọn trên TreeCategories, nếu có). Sau khi chèn, báo luôn đường dẫn file chẩn đoán .txt
        // (kích thước + kiểm tra overlap từng view) để user gửi lại đánh giá thay vì phải chụp màn hình.
        // =========================================================
        private void BtnInsertFittingTable_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("No active project. Load a folder first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Nếu user đang chọn 1 Category cụ thể trên TreeCategories (khác "All Fittings"), chỉ lấy
            // fitting trong category đó — mirror đúng logic ApplyFilters().
            IEnumerable<CatalogItem> candidateSource = _fullCatalog;
            if (TreeCategories.SelectedItem is CategoryNode selectedNode && selectedNode.CategoryName != "All Fittings")
                candidateSource = selectedNode.Items;

            var projectItems = candidateSource.Where(i => i.EntityType == "Block").ToList();
            if (projectItems.Count == 0)
            {
                MessageBox.Show("No fitting in the selected category.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Title bảng = tên Category user đang chọn (vì 1 bản vẽ có thể chứa nhiều Fitting Table,
            // mỗi bảng ứng với 1 category khác nhau); "All Fittings" (không chọn category cụ thể) ->
            // fallback tên Project Folder.
            string tableTitle = (TreeCategories.SelectedItem is CategoryNode titleNode && titleNode.CategoryName != "All Fittings")
                ? titleNode.CategoryName
                : _projectContext.ProjectDisplayName;

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try
            {
                // Bỏ thông báo pop-up sau khi chèn (theo yêu cầu) — report chẩn đoán vẫn được service
                // ghi ra file nếu cần tra cứu; ở đây chỉ chèn bảng, không hiển thị dialog.
                _fittingService.InsertFittingTable(projectItems, tableTitle);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadCatalog();

        // =========================================================
        // Edit Properties — mở VirtualItemWindow ở chế độ edit cho item(s) đang chọn (Master fields)
        // Cho phép chọn nhiều item cùng lúc (cùng Name, khác View)
        // =========================================================
        private void BtnEditProperties_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select item(s) to edit.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Chỉ cho edit item được add từ CAD (Inventor block không sửa tay)
            if (selected.Any(i => i.Source == "Inventor"))
            {
                MessageBox.Show("Inventor items are managed by the import process and cannot be edited here.\nDeselect Inventor items and try again.",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                VirtualItemWindow editWin = selected.Count == 1
                    ? new VirtualItemWindow(_masterService, selected[0], isEditMode: true)
                    : new VirtualItemWindow(_masterService, selected);
                editWin.Owner = this;
                if (editWin.ShowDialog() == true)
                    LoadCatalog();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =========================================================
        // Edit Pos Num — sửa ProjectPosNum, ghi thẳng vào catalog của Project Folder đang active.
        // =========================================================
        private void BtnEditPosNum_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("No active project. Load a folder first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                    _masterService.MergeIntoMaster(selected);
                    foreach (var it in selected) _recentTracker?.Track(it.BlockName);
                    LoadCatalog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =========================================================
        // Edit View Type — sửa IsPlanView/CountPlanViewOnly, ghi thẳng vào catalog.
        // Chỉ áp dụng Block-type (Accessory không có khái niệm "view").
        // =========================================================
        private void BtnEditViewType_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("No active project. Load a folder first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().Where(i => i.EntityType == "Block").ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select Block-type item(s) to edit (Edit View Type doesn't apply to Accessories).",
                    "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dlg = new EditViewTypeDialog(selected) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _masterService.MergeIntoMaster(selected);
                    foreach (var it in selected) _recentTracker?.Track(it.BlockName);
                    LoadCatalog();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =========================================================
        // Remove from Project — xoá vĩnh viễn khỏi FittingCatalog.json của Project Folder đang active
        // =========================================================
        private void BtnRemoveFromMaster_Click(object sender, RoutedEventArgs e)
        {
            var selected = GridCatalog.SelectedItems.Cast<CatalogItem>().ToList();
            if (selected.Count == 0) return;

            if (MessageBox.Show($"Permanently remove {selected.Count} item(s) from this Project?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

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
                $"Push current drawing definition of {blockItems.Count} block(s) back to the Project Folder?\n\n" +
                "This will OVERWRITE the .dwg file(s) in the Project Folder with the block definition\n" +
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

        // =========================================================
        // Sync — đồng bộ property của catalog xuống drawing đang mở.
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

        // =========================================================
        // Auto-Assign Pos
        // =========================================================
        private void BtnAutoAssignPos_Click(object sender, RoutedEventArgs e)
        {
            if (!_projectContext.HasActiveProject)
            {
                MessageBox.Show("Load a folder first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new AutoAssignStartDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            try
            {
                int groups = _masterService.AutoAssignPositions(dlg.StartNumber);
                LoadCatalog();
                MessageBox.Show($"Auto-assigned {groups} position group(s).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private void ShowPushUpdateResult(Models.FittingManagement.PushUpdateResult result, string title = "Push Update Result")
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
    }
}
