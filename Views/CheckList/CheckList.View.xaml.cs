using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MCGCadPlugin.Services.CheckList;
using MCGCadPlugin.Models.CheckList;

namespace MCGCadPlugin.Views.CheckList
{
    public partial class QaChecklistView : UserControl
    {
        private AutoCadService _acService;
        private ChecklistDocument _currentDoc;

        public QaChecklistView()
        {
            InitializeComponent();
            
            // Chỉ khởi tạo AutoCad Service
            _acService = new AutoCadService();
            
            // Đổ danh sách Discipline
            CboDiscipline.ItemsSource = ChecklistDatabase.Disciplines;
            if (CboDiscipline.Items.Count > 0) CboDiscipline.SelectedIndex = 0;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
        }

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
                // [AUTO-PURGE]: Gọi hàm sát thủ diệt hàng giả của AutoCAD
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

        private void BtnOpenChecklist_Click(object sender, RoutedEventArgs e)
        {
            string selectedDisc = CboDiscipline.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedDisc)) return;

            try
            {
                // Chỉ truyền AutoCadService
                ChecklistWindow window = new ChecklistWindow(_acService, _currentDoc, selectedDisc);
                
                // Mở cửa sổ theo cách của AutoCAD
                bool? result = Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(window);

                if (result == true) RefreshStatus();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening checklist window: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    RefreshStatus();
                    MessageBox.Show("QA/QC data and CAD stamps cleared successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}