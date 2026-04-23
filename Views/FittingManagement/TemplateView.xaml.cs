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
            bool pullFromVault = true;

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

            // Vault breakdown — hiển thị nếu có Vault results.
            string vaultSection = BuildVaultBreakdown(result);
            if (!string.IsNullOrEmpty(vaultSection))
                message += "\n\n" + vaultSection;

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

        /// <summary>
        /// Build Vault refresh breakdown section cho dialog kết quả.
        /// Group theo Status: Success/AlreadyLatest → ✓, SkippedNot* → ⚠, Failed → ✗.
        /// Liệt kê từng file trong mỗi nhóm để user verify rõ ràng.
        /// </summary>
        private string BuildVaultBreakdown(ImportResult result)
        {
            if (result.VaultResults == null || result.VaultResults.Count == 0)
                return null;

            var successGroup = result.VaultResults
                .Where(r => r.IsSuccess).ToList();
            var skipNotInVault = result.VaultResults
                .Where(r => r.Status == VaultRefreshStatus.SkippedNotInVault).ToList();
            var skipNoAddIn = result.VaultResults
                .Where(r => r.Status == VaultRefreshStatus.SkippedNoAddIn).ToList();
            var skipNotLoggedIn = result.VaultResults
                .Where(r => r.Status == VaultRefreshStatus.SkippedNotLoggedIn).ToList();
            var failedGroup = result.VaultResults
                .Where(r => r.Status == VaultRefreshStatus.Failed).ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("── Vault refresh ──");

            if (successGroup.Count > 0)
            {
                sb.AppendLine($"✓ Pulled latest: {successGroup.Count} file(s)");
                foreach (var r in successGroup.Take(5))
                    sb.AppendLine($"  • {System.IO.Path.GetFileName(r.FilePath)}" +
                                  (string.IsNullOrEmpty(r.MethodUsed) ? "" : $" (via {r.MethodUsed})"));
                if (successGroup.Count > 5)
                    sb.AppendLine($"  ... và {successGroup.Count - 5} file(s) khác");
            }

            if (skipNotInVault.Count > 0)
            {
                sb.AppendLine($"⚠ Not in vault: {skipNotInVault.Count} file(s) — dùng file local");
                foreach (var r in skipNotInVault.Take(3))
                    sb.AppendLine($"  • {System.IO.Path.GetFileName(r.FilePath)}");
                if (skipNotInVault.Count > 3)
                    sb.AppendLine($"  ... và {skipNotInVault.Count - 3} file(s) khác");
            }

            if (skipNoAddIn.Count > 0)
            {
                // Lý do skipNoAddIn thường đồng loạt cho cả batch → chỉ cần 1 dòng.
                sb.AppendLine($"⚠ Vault AddIn không available: {skipNoAddIn.Count} file(s) dùng local");
                sb.AppendLine($"  Lý do: {skipNoAddIn[0].Message}");
            }

            if (skipNotLoggedIn.Count > 0)
            {
                sb.AppendLine($"⚠ Vault chưa login: {skipNotLoggedIn.Count} file(s) dùng local");
                sb.AppendLine($"  → Mở Inventor → Vault menu → Log In, rồi import lại để get latest.");
            }

            if (failedGroup.Count > 0)
            {
                sb.AppendLine($"✗ Vault lỗi: {failedGroup.Count} file(s) dùng local");
                foreach (var r in failedGroup.Take(3))
                    sb.AppendLine($"  • {System.IO.Path.GetFileName(r.FilePath)}: {r.Message}");
                if (failedGroup.Count > 3)
                    sb.AppendLine($"  ... và {failedGroup.Count - 3} file(s) khác (xem log).");
            }

            return sb.ToString().TrimEnd();
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
