using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Prompt hiển thị khi import IDW phát hiện fitting đã tồn tại trong Master Catalog
    /// (trùng Part Number hoặc trùng Block Name sẽ được tạo). User chọn Overwrite từng dòng
    /// hoặc bấm Cancel Import để hủy toàn bộ batch.
    /// </summary>
    public partial class ImportDuplicateDialog : Window
    {
        internal List<ImportDuplicateItem> Items { get; }

        internal ImportDuplicateDialog(List<Tuple<FittingManagementService.ExtractedIdw, CatalogItem, string>> duplicates)
        {
            InitializeComponent();

            Items = duplicates.Select(d => new ImportDuplicateItem
            {
                Extracted = d.Item1,
                FileName = d.Item1.SourceIdwName,
                MatchReason = d.Item3,
                ExistingPartNumber = d.Item2.PartNumber,
                ExistingTitle = d.Item2.Title,
                ExistingBlockName = d.Item2.BlockName,
                ExistingCreatedDate = d.Item2.CreatedDate,
                Overwrite = false
            }).ToList();

            GridDuplicates.ItemsSource = Items;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>Dòng hiển thị trong <see cref="ImportDuplicateDialog"/> — 1 file IDW trùng với 1 entry Master Catalog.</summary>
    internal class ImportDuplicateItem
    {
        public FittingManagementService.ExtractedIdw Extracted { get; set; }
        public string FileName { get; set; }
        public string MatchReason { get; set; }
        public string ExistingPartNumber { get; set; }
        public string ExistingTitle { get; set; }
        public string ExistingBlockName { get; set; }
        public string ExistingCreatedDate { get; set; }
        public bool Overwrite { get; set; }
    }
}
