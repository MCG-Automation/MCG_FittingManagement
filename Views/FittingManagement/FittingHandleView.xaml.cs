using System;
using System.Windows;
using System.Windows.Controls;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Tab "Fitting Handle" — các công cụ người dùng cuối tương tác với fitting đã có trong bản vẽ:
    /// xuất BOM + place/auto-balloon.
    /// </summary>
    public partial class FittingHandleView : UserControl
    {
        private readonly IFittingManagementService _service;

        public FittingHandleView()
        {
            InitializeComponent();
            _service = new FittingManagementService();
        }

        private static BomPreviewWindow _bomWin;

        private void BtnOpenBomPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bomWin != null && _bomWin.IsLoaded)
                {
                    _bomWin.Activate();
                    return;
                }
                _bomWin = new BomPreviewWindow(_service);
                _bomWin.Closed += (_, __) => _bomWin = null;
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(_bomWin);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddBalloon_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.InteractivePlaceBalloon(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnMassBalloon_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.MassAutoBalloon(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
