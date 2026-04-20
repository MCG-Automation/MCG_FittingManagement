using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MCGCadPlugin.Commands;
using MCGCadPlugin.Models.CheckList;
using MCGCadPlugin.Services.CheckList;

namespace MCGCadPlugin.Views.CheckList
{
    public partial class QaChecklistView : UserControl
    {
        #region Fields
        private const string LOG_PREFIX = "[QaChecklistView]";

        private readonly AutoCadService _acService;
        private ChecklistDocument _currentDoc;

        /// <summary>Danh sách item đang hiển thị trên panel checklist inline</summary>
        public ObservableCollection<ChecklistItem> ChecklistData { get; private set; }
        #endregion

        #region Constructor
        public QaChecklistView()
        {
            InitializeComponent();

            _acService = new AutoCadService();

            // Đổ danh sách Discipline vào ComboBox
            CboDiscipline.ItemsSource = ChecklistDatabase.Disciplines;
            if (CboDiscipline.Items.Count > 0) CboDiscipline.SelectedIndex = 0;
        }
        #endregion

        #region Lifecycle
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
        }
        #endregion

        #region Status Panel
        private void RefreshStatus()
        {
            _currentDoc = _acService.LoadChecklistFromDwg();

            if (_currentDoc != null && _currentDoc.Status == "APPROVED")
            {
                CboDiscipline.SelectedItem = _currentDoc.Discipline;
                CboDiscipline.IsEnabled = false;

                BorderStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8E6C9"));
                BorderStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                TxtStatus.Text = "APPROVED (READY FOR RELEASE)";
                TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1B5E20"));

                GridApprovedInfo.Visibility = Visibility.Visible;
                TxtApprovedInfo.Text = $"{_currentDoc.ApprovedBy} at {_currentDoc.ApprovedDate}";
            }
            else
            {
                // [AUTO-PURGE]: Xóa các QA Stamp giả còn sót trong bản vẽ
                _acService.PurgeFakeQaStamps();
                CboDiscipline.IsEnabled = true;
                ResetUIStatus();
            }
        }

        private void ResetUIStatus()
        {
            BorderStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFE0B2"));
            BorderStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFB300"));
            TxtStatus.Text = "PENDING / NOT STARTED";
            TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
            GridApprovedInfo.Visibility = Visibility.Collapsed;
        }

        /// <summary>Đóng toàn bộ PaletteSet khi user bấm nút X</summary>
        private void BtnClosePalette_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PaletteManager.Instance.Hide();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI khi đóng Palette: {ex.Message}");
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to clear all QA/QC data from this drawing?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (_acService.DeleteChecklistFromDwg())
                {
                    _currentDoc = null;
                    _acService.PurgeFakeQaStamps();
                    HideChecklistPanel();
                    RefreshStatus();
                    MessageBox.Show("QA/QC data and CAD stamps cleared successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        #endregion

        #region Checklist Panel (inline)
        private void BtnOpenChecklist_Click(object sender, RoutedEventArgs e)
        {
            string selectedDisc = CboDiscipline.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisc)) return;

            try
            {
                ShowChecklistPanel(selectedDisc);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI khi mở checklist: {ex.Message}");
                MessageBox.Show($"Error opening checklist: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Nạp dữ liệu và hiển thị panel checklist inline</summary>
        private void ShowChecklistPanel(string discipline)
        {
            // Dùng lại document đã có hoặc tạo mới từ default items
            if (_currentDoc == null)
            {
                _currentDoc = new ChecklistDocument { Discipline = discipline };
                _currentDoc.Items = ChecklistDatabase.GetDefaultItems(discipline);
            }
            else if (_currentDoc.Status != "APPROVED")
            {
                // Nếu user đổi discipline thì nạp lại danh sách mặc định tương ứng
                if (!string.Equals(_currentDoc.Discipline, discipline, StringComparison.OrdinalIgnoreCase))
                {
                    _currentDoc.Discipline = discipline;
                    _currentDoc.Items = ChecklistDatabase.GetDefaultItems(discipline);
                }
            }

            TxtHeader.Text = $"{discipline.ToUpper()} CHECKLIST";

            ChecklistData = new ObservableCollection<ChecklistItem>(_currentDoc.Items);
            ListItems.ItemsSource = ChecklistData;

            // Khóa chỉnh sửa khi bản vẽ đã được duyệt
            bool isApproved = _currentDoc.Status == "APPROVED";
            ListItems.IsEnabled = !isApproved;
            TxtNewItem.IsEnabled = !isApproved;
            BtnAddItem.IsEnabled = !isApproved;
            BtnSaveDraft.Visibility = isApproved ? Visibility.Collapsed : Visibility.Visible;
            BtnSignApprove.Content = isApproved ? "ALREADY APPROVED" : "SIGN & APPROVE";

            PanelChecklist.Visibility = Visibility.Visible;
            UpdateProgress();
        }

        private void HideChecklistPanel()
        {
            PanelChecklist.Visibility = Visibility.Collapsed;
            ListItems.ItemsSource = null;
            ChecklistData = null;
        }

        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateProgress();
        }

        /// <summary>
        /// Handler cho N/A checkbox (chỉ xuất hiện ở custom items).
        /// Mutual exclusion giữa IsChecked và IsNotApplicable được xử lý ở setter của Model.
        /// </summary>
        private void NaCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateProgress();
        }

        /// <summary>
        /// Tính tiến độ hoàn thành checklist.
        /// Một item được coi là "done" khi IsChecked = true HOẶC IsNotApplicable = true.
        /// N/A chỉ áp dụng cho custom items — fixed items bắt buộc phải IsChecked.
        /// </summary>
        private void UpdateProgress()
        {
            if (ChecklistData == null || ChecklistData.Count == 0)
            {
                ProgBar.Value = 0;
                TxtProgress.Text = "0 / 0 (0%)";
                BtnSignApprove.IsEnabled = false;
                return;
            }

            int total = ChecklistData.Count;
            int doneCount = ChecklistData.Count(x => x.IsChecked || x.IsNotApplicable);
            double percentage = ((double)doneCount / total) * 100;

            ProgBar.Value = percentage;
            TxtProgress.Text = $"{doneCount} / {total} ({Math.Round(percentage)}%)";

            if (_currentDoc != null && _currentDoc.Status != "APPROVED")
            {
                BtnSignApprove.IsEnabled = (doneCount == total);
                BtnSignApprove.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(doneCount == total ? "#FF005E9E" : "#FF4CAF50"));
            }
        }

        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            string newContent = TxtNewItem.Text.Trim();
            if (string.IsNullOrEmpty(newContent)) return;

            var newItem = new ChecklistItem(newContent, isCustom: true);
            ChecklistData.Add(newItem);

            TxtNewItem.Clear();
            UpdateProgress();
        }

        private void BtnDeleteCustomItem_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || btn.Tag == null) return;

            string idToDelete = btn.Tag.ToString();
            var item = ChecklistData.FirstOrDefault(x => x.Id == idToDelete);

            if (item != null)
            {
                ChecklistData.Remove(item);
                UpdateProgress();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            HideChecklistPanel();
        }

        private void BtnSaveDraft_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDoc == null || ChecklistData == null) return;

            _currentDoc.Items = ChecklistData.ToList();
            if (_acService.SaveChecklistToDwg(_currentDoc))
            {
                HideChecklistPanel();
                RefreshStatus();
            }
        }

        private void BtnSignApprove_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDoc == null || ChecklistData == null) return;

            if (MessageBox.Show(
                    "Approving this drawing will generate a QA Stamp and lock the checklist.\n\nDo you want to proceed?",
                    "Confirm Approval",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _currentDoc.Items = ChecklistData.ToList();
            _currentDoc.Status = "APPROVED";
            _currentDoc.ApprovedBy = Environment.UserName;
            _currentDoc.ApprovedDate = DateTime.Now.ToString("dd/MMM/yyyy HH:mm");

            if (_acService.SaveChecklistToDwg(_currentDoc))
            {
                _acService.GenerateQaStamp();
                MessageBox.Show("Drawing successfully Approved and Signed!", "QA Passed", MessageBoxButton.OK, MessageBoxImage.Information);

                HideChecklistPanel();
                RefreshStatus();
            }
        }
        #endregion
    }
}
