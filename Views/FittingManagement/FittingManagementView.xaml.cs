using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Services.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Views.FittingManagement
{
    public partial class FittingManagementView : UserControl
    {
        // Khởi tạo Service qua Interface (Đúng chuẩn SOLID)
        private readonly IFittingManagementService _service;

        public FittingManagementView()
        {
            InitializeComponent();
            _service = new FittingManagementService(); 
        }

        // =========================================================
        // IDW IMPORT (gộp): Extract → Split View → Create Blocks
        // Async: Phase 1 (Inventor COM) chạy Task.Run để AutoCAD UI không bị khoá.
        // =========================================================
        private async void BtnBatchImportInventor_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select Inventor Drawing Files (.idw)",
                Filter = "Inventor Drawing (*.idw)|*.idw",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;

            // Xác định BomType từ RadioButton trên UI
            string bomType = (RadioPanelFitting.IsChecked == true) ? "PANEL" : "DETAIL";

            // UI feedback: disable button, wait cursor, status text
            BtnBatchImportInventor.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.AppStarting;
            var progress = new Progress<string>(msg => TxtImportStatus.Text = msg);

            try
            {
                ImportResult result = await _service.ImportIdwFilesAsync(ofd.FileNames, bomType, progress);
                TxtImportStatus.Text = $"Done. Success={result.SuccessCount}, Failed={result.FailCount}";
                ShowImportResultDialog("Import IDW", result);
            }
            catch (Exception ex)
            {
                TxtImportStatus.Text = "Error. See log.";
                ShowExceptionDialog("Lỗi Import IDW", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnBatchImportInventor.IsEnabled = true;
            }
        }

        /// <summary>
        /// Hien thi dialog ket qua import voi chi tiet loi tung file.
        /// Nut "Open Log" mo file log de user gui cho team dev.
        /// </summary>
        private void ShowImportResultDialog(string title, ImportResult result)
        {
            string message = $"{title} hoàn tất!\n\n" +
                             $"✓ Thành công: {result.SuccessCount}\n" +
                             $"✗ Thất bại:   {result.FailCount}";

            if (result.FailCount > 0 && result.Errors.Count > 0)
            {
                // Hien thi toi da 8 loi dau tien - tranh dialog qua dai
                int maxErrorsToShow = 8;
                var errorsToShow = result.Errors.Take(maxErrorsToShow).ToList();

                message += "\n\n── Chi tiết lỗi ──\n" + string.Join("\n", errorsToShow);

                if (result.Errors.Count > maxErrorsToShow)
                    message += $"\n\n... và {result.Errors.Count - maxErrorsToShow} lỗi khác (xem trong file log).";

                message += $"\n\n── Log chi tiết ──\n{FileLogger.LogPath}" +
                           "\n\nNhấn Yes để mở thư mục chứa log, No để đóng.";

                var answer = MessageBox.Show(message, $"{title} Result",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (answer == MessageBoxResult.Yes)
                    OpenLogFolder();
            }
            else
            {
                MessageBox.Show(message, $"{title} Result",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Hien thi dialog khi exception bubble up (loi ngoai pham vi per-file).
        /// </summary>
        private void ShowExceptionDialog(string title, Exception ex)
        {
            string message = $"Lỗi: {ex.Message}\n\n" +
                             $"Chi tiết lỗi đã được ghi vào file log:\n{FileLogger.LogPath}" +
                             "\n\nNhấn Yes để mở thư mục chứa log.";

            var answer = MessageBox.Show(message, title,
                MessageBoxButton.YesNo, MessageBoxImage.Error);

            if (answer == MessageBoxResult.Yes)
                OpenLogFolder();
        }

        /// <summary>
        /// Mo Windows Explorer tai thu muc chua file log va highlight file log.
        /// </summary>
        private void OpenLogFolder()
        {
            try
            {
                if (System.IO.File.Exists(FileLogger.LogPath))
                    Process.Start("explorer.exe", $"/select,\"{FileLogger.LogPath}\"");
                else
                    Process.Start("explorer.exe", FileLogger.LogDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể mở thư mục log: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =========================================================
        // STEP 3: FITTING LIBRARY
        // =========================================================
        private void BtnOpenLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                FittingLibraryWindow libraryWindow = new FittingLibraryWindow(_service);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(libraryWindow);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =========================================================
        // STEP 4: BOM EXPORT & BALLOONING
        // =========================================================
        private void BtnOpenBomPreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BomPreviewWindow bomWindow = new BomPreviewWindow(_service);
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(bomWindow);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
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

        // =========================================================
        // BLOCK UTILITIES EVENTS
        // =========================================================
        private void BtnRenameCloneBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.InteractiveBlockRenameClone(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnRedefineBlocks_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.RedefineBlocksFromLibrary(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnSmartReplace_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.SmartReplaceBlocks(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnChangeBasePoint_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.ChangeBlockBasePoint(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnAddToBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.AddEntitiesToBlock(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnExtractFromBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.ExtractEntitiesFromBlock(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}