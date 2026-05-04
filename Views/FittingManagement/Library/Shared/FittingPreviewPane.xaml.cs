using System;
using System.Windows;
using System.Windows.Controls;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Services.FittingManagement;

namespace MCGCadPlugin.Views.FittingManagement
{
    /// <summary>
    /// Pane hiển thị preview thumbnail + properties cho 1 <see cref="CatalogItem"/>.
    /// Dùng chung cho MasterLibraryWindow + ProjectLibraryWindow.
    /// Item type không phải "Block" → ẩn pane (parent điều khiển Visibility).
    /// </summary>
    public partial class FittingPreviewPane : UserControl
    {
        private IFittingPreviewService _previewService;

        public FittingPreviewPane()
        {
            InitializeComponent();
            ShowEmpty("Select a Block item to preview");
        }

        /// <summary>Inject preview service (do parent window khởi tạo + chia sẻ).</summary>
        public void Initialize(IFittingPreviewService previewService)
        {
            _previewService = previewService;
        }

        /// <summary>Hiển thị item — load preview + properties. Null thì show empty state.</summary>
        public void ShowItem(CatalogItem item)
        {
            if (item == null)
            {
                ShowEmpty("Select a Block item to preview");
                return;
            }

            if (!IsBlockType(item))
            {
                ShowEmpty($"No preview for {item.EntityType ?? "Unknown"} entity type.");
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;

            // Properties
            TxtPartNumber.Text = string.IsNullOrEmpty(item.PartNumber) ? "(no part number)" : item.PartNumber;
            TxtTitle.Text = item.Title ?? "";
            TxtDescription.Text = item.Description ?? "—";
            TxtMass.Text = string.IsNullOrEmpty(item.Mass) ? "—" : $"{item.Mass} kg";
            TxtMaterial.Text = string.IsNullOrEmpty(item.Material) ? "—" : item.Material;
            TxtDesigner.Text = string.IsNullOrEmpty(item.Designer) ? "—" : item.Designer;
            TxtRevision.Text = string.IsNullOrEmpty(item.Revision) ? "—" : item.Revision;
            TxtUoM.Text = string.IsNullOrEmpty(item.UoM) ? "—" : item.UoM;
            TxtBlockName.Text = item.BlockName ?? "";

            // Image
            var bmp = _previewService?.GetPreview(item);
            if (bmp != null)
            {
                PreviewImage.Source = bmp;
                PreviewImage.Visibility = Visibility.Visible;
                NoPreviewText.Visibility = Visibility.Collapsed;
            }
            else
            {
                PreviewImage.Source = null;
                PreviewImage.Visibility = Visibility.Collapsed;
                NoPreviewText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>Reset pane về empty state với message tuỳ chỉnh.</summary>
        public void Clear() => ShowEmpty("Select a Block item to preview");

        private void ShowEmpty(string message)
        {
            EmptyStateText.Text = message;
            EmptyState.Visibility = Visibility.Visible;
            PreviewImage.Source = null;
        }

        private static bool IsBlockType(CatalogItem item)
        {
            return string.Equals(item.EntityType, "Block", StringComparison.OrdinalIgnoreCase);
        }
    }
}
