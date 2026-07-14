using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Tab "Template" — admin workflow: import .idw thành template + mở Library để duyệt template.
    /// Import: Phase 1 (Inventor COM) chạy worker thread, Phase 2 (AutoCAD db) quay về UI thread sau await.
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
        // IDW IMPORT — Extract → Split View → Create Blocks → Publish vào Project Folder đang active.
        // Nếu chưa có Project Folder nào active, hỏi user chọn (hoặc tạo mới) 1 lần — các lần import
        // sau dùng lại đúng folder đó, không hỏi lại (đúng mô hình "1 Project = 1 Folder").
        // =========================================================
        private async void BtnBatchImportInventor_Click(object sender, RoutedEventArgs e)
        {
            if (!ActiveProjectContext.Instance.HasActiveProject)
            {
                string chosen = FittingTableWindow.PromptSelectProjectFolder();
                if (chosen == null) return;
                ActiveProjectContext.Instance.ProjectFolderPath = chosen;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select Inventor Drawing Files (.idw)",
                Filter = "Inventor Drawing (*.idw)|*.idw",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;

            string bomType = (RadioPanelFitting.IsChecked == true) ? "EQUIPMENT" : "HULL";
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
                ShowExceptionDialog("Import IDW Error", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnBatchImportInventor.IsEnabled = true;
            }
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

        // =========================================================
        // HELPERS — hiển thị kết quả / lỗi
        // =========================================================

        /// <summary>
        /// Hien thi dialog ket qua import voi chi tiet loi tung file.
        /// Nut "Open Log" mo file log de user gui cho team dev.
        /// </summary>
        private void ShowImportResultDialog(string title, ImportResult result)
        {
            string message = $"{title} complete.\n\n" +
                             $"✓ Success: {result.SuccessCount}\n" +
                             $"✗ Failed:  {result.FailCount}";

            // Vault breakdown — hiển thị nếu có Vault results.
            string vaultSection = BuildVaultBreakdown(result);
            if (!string.IsNullOrEmpty(vaultSection))
                message += "\n\n" + vaultSection;

            if (result.FailCount > 0 && result.Errors.Count > 0)
            {
                int maxErrorsToShow = 8;
                var errorsToShow = result.Errors.Take(maxErrorsToShow).ToList();

                message += "\n\n── Error details ──\n" + string.Join("\n", errorsToShow);

                if (result.Errors.Count > maxErrorsToShow)
                    message += $"\n\n... and {result.Errors.Count - maxErrorsToShow} more (see log).";

                message += $"\n\n── Log ──\n{FileLogger.LogPath}" +
                           "\n\nClick Yes to open the log folder, No to close.";

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
            sb.AppendLine("── Vault status ──");

            if (successGroup.Count > 0)
            {
                sb.AppendLine($"✓ Updated to latest: {successGroup.Count} file(s)");
                foreach (var r in successGroup.Take(5))
                    sb.AppendLine($"  • {System.IO.Path.GetFileName(r.FilePath)}");
                if (successGroup.Count > 5)
                    sb.AppendLine($"  ... and {successGroup.Count - 5} more");
            }

            if (skipNotInVault.Count > 0)
            {
                sb.AppendLine($"⚠ Not in Vault: {skipNotInVault.Count} file(s) — used local copy");
                foreach (var r in skipNotInVault.Take(3))
                    sb.AppendLine($"  • {System.IO.Path.GetFileName(r.FilePath)}");
                if (skipNotInVault.Count > 3)
                    sb.AppendLine($"  ... and {skipNotInVault.Count - 3} more");
            }

            if (skipNoAddIn.Count > 0)
            {
                sb.AppendLine($"⚠ Vault SDK not available: {skipNoAddIn.Count} file(s) — used local copy");
                sb.AppendLine($"  → Install Autodesk Vault Client to enable Vault refresh.");
            }

            if (skipNotLoggedIn.Count > 0)
            {
                sb.AppendLine($"⚠ Not signed in to Vault: {skipNotLoggedIn.Count} file(s) — used local copy");
                sb.AppendLine($"  → Open \"Autodesk Vault\" (Vault Explorer), sign in, then try again.");
            }

            if (failedGroup.Count > 0)
            {
                sb.AppendLine($"✗ Vault error: {failedGroup.Count} file(s) — used local copy");
                foreach (var r in failedGroup.Take(3))
                    sb.AppendLine($"  • {System.IO.Path.GetFileName(r.FilePath)}: {r.Message}");
                if (failedGroup.Count > 3)
                    sb.AppendLine($"  ... and {failedGroup.Count - 3} more (see log).");
            }

            return sb.ToString().TrimEnd();
        }

        private void ShowExceptionDialog(string title, Exception ex)
        {
            string message = $"Error: {ex.Message}\n\n" +
                             $"Full details in log:\n{FileLogger.LogPath}" +
                             "\n\nClick Yes to open the log folder.";

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
                MessageBox.Show($"Cannot open log folder: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
