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

        private static BomPreviewWindow _equipmentBomWin;
        private static BomPreviewWindow _hullBomWin;

        private void BtnOpenEquipmentBom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_equipmentBomWin != null && _equipmentBomWin.IsLoaded)
                {
                    _equipmentBomWin.Activate();
                    return;
                }
                _equipmentBomWin = new BomPreviewWindow(_service, BomMode.Equipment);
                _equipmentBomWin.Closed += (_, __) => _equipmentBomWin = null;
                // .Show() thay ShowModelessWindow — xem giải thích ở FittingTableWindow.ShowOrActivate:
                // để cửa sổ tự lùi ra sau khi user click vào bản vẽ CAD (không bị owned/nổi mãi trên CAD).
                _equipmentBomWin.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenHullBom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_hullBomWin != null && _hullBomWin.IsLoaded)
                {
                    _hullBomWin.Activate();
                    return;
                }
                _hullBomWin = new BomPreviewWindow(_service, BomMode.Hull);
                _hullBomWin.Closed += (_, __) => _hullBomWin = null;
                // .Show() thay ShowModelessWindow — để cửa sổ tự lùi ra sau khi user click vào bản vẽ CAD.
                _hullBomWin.Show();
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
