using System;
using System.Windows;
using System.Windows.Controls;
using MCGCadPlugin.Services.FittingManagement;

namespace MCGCadPlugin.Views.FittingManagement
{
    /// <summary>
    /// Tab "Project Config" — setup cho project: mở Project Library để duyệt/chèn fitting đã cấu hình cho project hiện tại.
    /// </summary>
    public partial class ProjectConfigView : UserControl
    {
        private readonly FittingManagementService _serviceImpl;
        private readonly IFittingManagementService _service;
        private readonly IProjectLibraryService _projectService;

        public ProjectConfigView()
        {
            InitializeComponent();
            _serviceImpl = new FittingManagementService();
            _service = _serviceImpl;
            _projectService = _serviceImpl;
        }

        private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ProjectLibraryWindow(_projectService, _service);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(win);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
