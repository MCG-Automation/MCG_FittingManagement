using System;
using System.Windows;
using System.Windows.Controls;
using MCGCadPlugin.Services.FittingManagement;

namespace MCGCadPlugin.Views.FittingManagement
{
    /// <summary>
    /// Tab "Project Config" — setup cho project: mở Fitting Library để duyệt/chèn fitting đã cấu hình.
    /// </summary>
    public partial class ProjectConfigView : UserControl
    {
        private readonly IFittingManagementService _service;

        public ProjectConfigView()
        {
            InitializeComponent();
            _service = new FittingManagementService();
        }

        private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FittingLibraryWindow libraryWindow = new FittingLibraryWindow(_service);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(libraryWindow);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
