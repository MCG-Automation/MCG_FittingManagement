using System;
using System.Windows;
using System.Windows.Controls;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Pane hiển thị preview thumbnail + properties cho 1 <see cref="CatalogItem"/>.
    /// Dùng trong FittingTableWindow.
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
                ShowEmpty("Select an item to preview");
                return;
            }

            if (!IsBlockType(item))
            {
                ShowMetadataCard(item);
                return;
            }

            // Block item — hiện image + properties panel
            MetadataCard.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;

            TxtPartNumber.Text = string.IsNullOrEmpty(item.PartNumber) ? "(no part number)" : item.PartNumber;
            TxtTitle.Text      = item.Title ?? "";
            TxtDescription.Text = item.Description ?? "—";
            TxtMass.Text       = string.IsNullOrEmpty(item.Mass) ? "—" : $"{item.Mass} kg";
            TxtMaterial.Text   = string.IsNullOrEmpty(item.Material) ? "—" : item.Material;
            TxtDesigner.Text   = string.IsNullOrEmpty(item.Designer) ? "—" : item.Designer;
            TxtRevision.Text   = string.IsNullOrEmpty(item.Revision) ? "—" : item.Revision;
            TxtUoM.Text        = string.IsNullOrEmpty(item.UoM) ? "—" : item.UoM;
            TxtBlockName.Text  = item.BlockName ?? "";

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

        /// <summary>Reset pane về empty state.</summary>
        public void Clear() => ShowEmpty("Select an item to preview");

        private void ShowEmpty(string message)
        {
            MetadataCard.Visibility = Visibility.Collapsed;
            EmptyStateText.Text = message;
            EmptyState.Visibility = Visibility.Visible;
            PreviewImage.Source = null;
        }

        /// <summary>
        /// Hiện metadata card cho non-block item (CAD-Linear: Polyline, Line, Circle...).
        /// Thay thế preview image bằng thông tin catalog dạng card.
        /// </summary>
        private void ShowMetadataCard(CatalogItem item)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            MetadataCard.Visibility = Visibility.Visible;

            CardEntityType.Text  = GetEntityLabel(item.EntityType);
            CardSource.Text      = string.IsNullOrEmpty(item.Source) ? "CAD" : item.Source;
            CardPartNumber.Text  = string.IsNullOrEmpty(item.PartNumber) ? "(no part number)" : item.PartNumber;
            CardTitle.Text       = item.Title ?? "";
            CardLayer.Text       = string.IsNullOrEmpty(item.TriggerLayer) ? "—" : item.TriggerLayer;
            CardUoM.Text         = string.IsNullOrEmpty(item.UoM) ? "—" : item.UoM;
            CardDescription.Text = string.IsNullOrEmpty(item.Description) ? "—" : item.Description;
            CardMass.Text        = string.IsNullOrEmpty(item.Mass) ? "—" : $"{item.Mass} kg/m";
            CardBomType.Text     = string.IsNullOrEmpty(item.BomType) ? "—" : item.BomType;
            CardDesigner.Text    = string.IsNullOrEmpty(item.Designer) ? "—" : item.Designer;
        }

        private static string GetEntityLabel(string entityType)
        {
            switch (entityType?.ToUpperInvariant())
            {
                case "POLYLINE":
                case "POLYLINE2D":
                case "POLYLINE3D": return "Linear Item  (Polyline)";
                case "LINE":       return "Linear Item  (Line)";
                case "CIRCLE":     return "Circular Item  (Circle)";
                case "ARC":        return "Arc Item";
                default:           return entityType ?? "Unknown Entity";
            }
        }

        private static bool IsBlockType(CatalogItem item)
            => string.Equals(item.EntityType, "Block", StringComparison.OrdinalIgnoreCase);
    }
}
