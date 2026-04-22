using System;
using System.Windows;
using System.Windows.Controls;
using MCGCadPlugin.Services.FittingManagement;

namespace MCGCadPlugin.Views.FittingManagement
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

        private void BtnOpenBomPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BomPreviewWindow bomWindow = new BomPreviewWindow(_service);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(bomWindow);
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
