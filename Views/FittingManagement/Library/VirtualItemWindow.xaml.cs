using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Services.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    public partial class VirtualItemWindow : Window
    {
        private readonly IMasterLibraryService _masterService;
        private CatalogItem _draftItem;

        public VirtualItemWindow(IMasterLibraryService masterService, CatalogItem draftItem)
        {
            InitializeComponent();
            _masterService = masterService;
            _draftItem = draftItem;
            LoadDraftDataToUI();
        }

        private void LoadDraftDataToUI()
        {
            if (_draftItem == null) return;

            TxtEntityType.Text = _draftItem.EntityType == "Block" ? $"Block: {_draftItem.BlockName}" : _draftItem.EntityType;
            TxtLayer.Text = _draftItem.TriggerLayer;
            TxtColor.Text = _draftItem.TriggerColor;
            TxtUoM.Text = _draftItem.UoM;

            TxtPartID.Text = _draftItem.PartNumber;
            TxtTitle.Text = !string.IsNullOrEmpty(_draftItem.Title)
                ? _draftItem.Title
                : HumanizeBlockName(_draftItem.BlockName);
            TxtDesc.Text = _draftItem.Description;
            TxtMass.Text = _draftItem.Mass ?? "0";

            if (!string.IsNullOrEmpty(_draftItem.BomType))
            {
                foreach (ComboBoxItem item in CboBomType.Items)
                {
                    if (item.Content.ToString().Equals(_draftItem.BomType, StringComparison.OrdinalIgnoreCase))
                    {
                        CboBomType.SelectedItem = item; break;
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPartID.Text))
            {
                MessageBox.Show("Part ID is required!", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPartID.Focus(); return;
            }

            _draftItem.PartNumber = TxtPartID.Text.Trim();
            _draftItem.Title      = TxtTitle.Text.Trim();
            _draftItem.Description = TxtDesc.Text.Trim();
            _draftItem.Mass       = TxtMass.Text.Trim();
            _draftItem.BomType    = (CboBomType.SelectedItem is ComboBoxItem cb) ? cb.Content.ToString() : "HULL";

            if (_draftItem.EntityType == "Block" && !string.IsNullOrEmpty(_draftItem.BlockName))
            {
                // Tier 2: export block ra .dwg để Insert hoạt động kể cả khi đổi drawing
                string filePath = Path.Combine(_masterService.MasterLibraryFolder, _draftItem.BlockName + ".dwg");
                _draftItem.FilePath = filePath;
                try
                {
                    var pushResult = _masterService.PushBlocksFromCurrentDrawing(new List<CatalogItem> { _draftItem });
                    if (pushResult.SuccessCount == 0)
                    {
                        // Block không xuất được → để rỗng, Tier 1 fallback sẽ xử lý khi Insert
                        _draftItem.FilePath = "";
                        Debug.WriteLine("[VirtualItemWindow] Block không export được — Insert dùng current drawing fallback.");
                    }
                }
                catch (Exception ex)
                {
                    _draftItem.FilePath = "";
                    Debug.WriteLine($"[VirtualItemWindow] Lỗi export block: {ex.Message}");
                }
            }
            else
            {
                // Non-Block entity (Line, Polyline…): không cần file
                _draftItem.BlockName = "";
                _draftItem.FilePath  = "";
            }

            try
            {
                _masterService.MergeIntoMaster(new List<CatalogItem> { _draftItem });
                MessageBox.Show("Saved to Master Library successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; this.Close();
        }

        /// <summary>Chuyển tên block thô thành label đọc được: bỏ prefix MCG_, thay _ - ; bằng space.</summary>
        private static string HumanizeBlockName(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName)) return "";
            // Lấy phần đầu tiên nếu có nhiều block (semicolon-separated)
            string name = blockName.Split(';')[0].Trim();
            // Bỏ prefix thường gặp
            foreach (string prefix in new[] { "MCG_", "MCG-" })
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                { name = name.Substring(prefix.Length); break; }
            }
            // Thay separator thành space, collapse khoảng trắng
            name = name.Replace('_', ' ').Replace('-', ' ').Replace(';', ' ');
            return System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"\s{2,}", " ");
        }
    }
}