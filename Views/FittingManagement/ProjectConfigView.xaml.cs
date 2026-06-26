using System;
using System.Windows;
using System.Windows.Controls;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
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

        private static ProjectLibraryWindow _projectLibWin;

        private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_projectLibWin != null && _projectLibWin.IsLoaded)
                {
                    _projectLibWin.Activate();
                    return;
                }
                _projectLibWin = new ProjectLibraryWindow(_projectService, _service);
                _projectLibWin.Closed += (_, __) => _projectLibWin = null;
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(_projectLibWin);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
