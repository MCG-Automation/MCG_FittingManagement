using System;
using System.Windows;
using System.Windows.Controls;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Tab "Fitting Table" — điểm vào chính của plugin. Chỉ còn 1 nhiệm vụ: mở cửa sổ Fitting Table
    /// (<see cref="FittingTableWindow"/>) — nơi tập trung mọi thao tác quản lý catalog: Import từ
    /// Inventor (.idw), Add from CAD, Copy to, chèn bảng... Import KHÔNG còn ở tab này (đã chuyển vào
    /// cửa sổ, cạnh "Add from CAD").
    /// </summary>
    public partial class TemplateView : UserControl
    {
        private readonly FittingManagementService _serviceImpl;
        private readonly IFittingManagementService _service;
        private readonly IMasterLibraryService _masterService;

        public TemplateView()
        {
            InitializeComponent();
            _serviceImpl = new FittingManagementService();
            _service = _serviceImpl;
            _masterService = _serviceImpl;
        }

        // =========================================================
        // OPEN FITTING TABLE (FittingTableWindow.ShowOrActivate tự quản lý singleton instance).
        // =========================================================
        private void BtnOpenMasterLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FittingTableWindow.ShowOrActivate(_masterService, _service);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
