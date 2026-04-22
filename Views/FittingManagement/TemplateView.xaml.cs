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
    /// <summary>
    /// Tab "Template" — admin workflow: import .idw thành template + mở Library để duyệt template.
    /// Import: Phase 1 (Inventor COM) chạy worker thread, Phase 2 (AutoCAD db) quay về UI thread sau await.
    /// </summary>
    public partial class TemplateView : UserControl
    {
        private readonly IFittingManagementService _service;

        public TemplateView()
        {
            InitializeComponent();
            _service = new FittingManagementService();
        }

        // =========================================================
        // IDW IMPORT — Extract → Split View → Create Blocks → Publish Library
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

            string bomType = (RadioPanelFitting.IsChecked == true) ? "PANEL" : "DETAIL";
            bool pullFromVault = (ChkPullFromVault.IsChecked == true);

            BtnBatchImportInventor.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.AppStarting;
            var progress = new Progress<string>(msg => TxtImportStatus.Text = msg);

            try
            {
                ImportResult result = await _service.ImportIdwFilesAsync(ofd.FileNames, bomType, pullFromVault, progress);
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

        // =========================================================
        // OPEN LIBRARY — duyệt template đã import
        // =========================================================
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

        // =========================================================
        // HELPERS — hiển thị kết quả / lỗi
        // =========================================================

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
    }
}
