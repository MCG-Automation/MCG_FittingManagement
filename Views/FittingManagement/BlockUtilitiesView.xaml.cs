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
    /// Tab "Block Utilities" — công cụ thao tác block + Drawing Collection:
    /// Rename, Redefine, Replace, Change Insertion Point, Add Objects, Extract Objects, Collect Drawings.
    /// </summary>
    public partial class BlockUtilitiesView : UserControl
    {
        private readonly IFittingManagementService _service;

        public BlockUtilitiesView()
        {
            InitializeComponent();
            _service = new FittingManagementService();
        }

        private void BtnRenameCloneBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.InteractiveBlockRenameClone(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnRedefineBlocks_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.RedefineBlocksFromLibrary(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnSmartReplace_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.SmartReplaceBlocks(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnChangeBasePoint_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.ChangeBlockBasePoint(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnAddToBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.AddEntitiesToBlock(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnExtractFromBlock_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            try { _service.ExtractEntitiesFromBlock(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =========================================================
        // DRAWING COLLECTION — gom nhiều .dwg vào bản vẽ hiện hành
        // =========================================================
        private async void BtnCollectDrawings_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select AutoCAD Drawings to Collect (.dwg)",
                Filter = "AutoCAD Drawing (*.dwg)|*.dwg",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;

            BtnCollectDrawings.IsEnabled = false;
            BtnCollectIdwDrawings.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.AppStarting;
            var progress = new Progress<string>(msg => TxtCollectionStatus.Text = msg);

            try
            {
                ImportResult result = await _service.CollectDrawingsAsync(ofd.FileNames, progress);
                TxtCollectionStatus.Text = $"Done. Success={result.SuccessCount}, Failed={result.FailCount}";
                ShowCollectionResultDialog(result);
            }
            catch (Exception ex)
            {
                TxtCollectionStatus.Text = "Error. See log.";
                FileLogger.LogException("[BlockUtilitiesView]", "BtnCollectDrawings_Click", ex);
                ShowExceptionDialog("Drawing Collection Error", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnCollectDrawings.IsEnabled = true;
                BtnCollectIdwDrawings.IsEnabled = true;
            }
        }

        private async void BtnCollectIdwDrawings_Click(object sender, RoutedEventArgs e)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Select Inventor Drawings to Collect (.idw)",
                Filter = "Inventor Drawing (*.idw)|*.idw",
                Multiselect = true
            };

            if (ofd.ShowDialog() != true || ofd.FileNames.Length == 0) return;

            BtnCollectDrawings.IsEnabled = false;
            BtnCollectIdwDrawings.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.AppStarting;
            var progress = new Progress<string>(msg => TxtCollectionStatus.Text = msg);

            try
            {
                ImportResult result = await _service.CollectIdwDrawingsAsync(ofd.FileNames, progress);
                TxtCollectionStatus.Text = $"Done. Success={result.SuccessCount}, Failed={result.FailCount}";
                ShowCollectionResultDialog(result);
            }
            catch (Exception ex)
            {
                TxtCollectionStatus.Text = "Error. See log.";
                FileLogger.LogException("[BlockUtilitiesView]", "BtnCollectIdwDrawings_Click", ex);
                ShowExceptionDialog("IDW Collection Error", ex);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                BtnCollectDrawings.IsEnabled = true;
                BtnCollectIdwDrawings.IsEnabled = true;
            }
        }

        // =========================================================
        // HELPERS — dialog kết quả / lỗi
        // =========================================================
        private void ShowCollectionResultDialog(ImportResult result)
        {
            const string title = "Drawing Collection";
            string message = $"{title} complete.\n\n" +
                             $"✓ Success: {result.SuccessCount}\n" +
                             $"✗ Failed:  {result.FailCount}";

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

                if (answer == MessageBoxResult.Yes) OpenLogFolder();
            }
            else
            {
                MessageBox.Show(message, $"{title} Result",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ShowExceptionDialog(string title, Exception ex)
        {
            string message = $"Error: {ex.Message}\n\n" +
                             $"Full details in log:\n{FileLogger.LogPath}" +
                             "\n\nClick Yes to open the log folder.";

            var answer = MessageBox.Show(message, title,
                MessageBoxButton.YesNo, MessageBoxImage.Error);

            if (answer == MessageBoxResult.Yes) OpenLogFolder();
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
