using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly bool _isEditMode;
        // Danh sách items khi edit nhiều cùng lúc (cùng Name, khác View)
        private readonly IList<CatalogItem> _editItems;

        /// <summary>Constructor cho Add mode và Single-edit mode.</summary>
        public VirtualItemWindow(IMasterLibraryService masterService, CatalogItem draftItem, bool isEditMode = false)
        {
            InitializeComponent();
            _masterService = masterService;
            _draftItem = draftItem;
            _isEditMode = isEditMode;
            if (_isEditMode)
            {
                Title = "Edit Properties";
                BtnSave.Content = "Save Changes";
            }
            LoadDraftDataToUI();
        }

        /// <summary>Constructor cho Multi-edit mode — edit nhiều item cùng lúc (cùng Name, khác View).</summary>
        public VirtualItemWindow(IMasterLibraryService masterService, IList<CatalogItem> editItems)
        {
            InitializeComponent();
            _masterService = masterService;
            _editItems = editItems;
            _draftItem = editItems[0];  // dùng item đầu làm template hiển thị
            _isEditMode = true;
            Title = $"Edit Properties — {editItems.Count} items";
            BtnSave.Content = "Save Changes";
            LoadDraftDataToUI();
        }

        private void LoadDraftDataToUI()
        {
            if (_draftItem == null) return;

            if (_editItems != null && _editItems.Count > 1)
            {
                // Multi-edit: tóm tắt thông tin geometry cho tất cả items
                string entitySummary = GetCommonValue(_editItems.Select(i => i.EntityType));
                TxtEntityType.Text = $"{_editItems.Count} items — {entitySummary}";
                TxtLayer.Text  = GetCommonValue(_editItems.Select(i => i.TriggerLayer));
                TxtColor.Text  = GetCommonValue(_editItems.Select(i => i.TriggerColor));
                TxtUoM.Text    = GetCommonValue(_editItems.Select(i => i.UoM));

                TxtPartID.Text = GetCommonValue(_editItems.Select(i => i.PartNumber));
                TxtTitle.Text  = GetCommonValue(_editItems.Select(i => i.Title));
                TxtDesc.Text   = GetCommonValue(_editItems.Select(i => i.Description));
                TxtMass.Text   = GetCommonValue(_editItems.Select(i => i.Mass)) ?? "0";
                SetBomTypeCombo(GetCommonValue(_editItems.Select(i => i.BomType)));
            }
            else
            {
                // Single item (Add mode hoặc Single-edit mode)
                TxtEntityType.Text = _draftItem.EntityType == "Block"
                    ? $"Block: {_draftItem.BlockName}"
                    : _draftItem.EntityType;
                TxtLayer.Text = _draftItem.TriggerLayer;
                TxtColor.Text = _draftItem.TriggerColor;
                TxtUoM.Text   = _draftItem.UoM;

                TxtPartID.Text = _draftItem.PartNumber;
                TxtTitle.Text  = !string.IsNullOrEmpty(_draftItem.Title)
                    ? _draftItem.Title
                    : HumanizeBlockName(_draftItem.BlockName);
                TxtDesc.Text  = _draftItem.Description;
                TxtMass.Text  = _draftItem.Mass ?? "0";
                SetBomTypeCombo(_draftItem.BomType);
            }
        }

        private void SetBomTypeCombo(string bomType)
        {
            if (string.IsNullOrEmpty(bomType)) return;
            foreach (ComboBoxItem item in CboBomType.Items)
            {
                if (item.Content.ToString().Equals(bomType, StringComparison.OrdinalIgnoreCase))
                { CboBomType.SelectedItem = item; break; }
            }
        }

        // Trả về giá trị chung nếu tất cả giống nhau, "(multiple)" nếu khác nhau
        private static string GetCommonValue(IEnumerable<string> values)
        {
            var distinct = values.Where(v => v != null).Distinct().ToList();
            if (distinct.Count == 0) return "";
            return distinct.Count == 1 ? distinct[0] : "(multiple)";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtPartID.Text))
            {
                MessageBox.Show("Part ID is required!", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPartID.Focus(); return;
            }

            if (_isEditMode)
            {
                // Edit mode: áp dụng metadata mới cho TẤT CẢ items, không re-export block
                IList<CatalogItem> itemsToSave = (_editItems != null && _editItems.Count > 0)
                    ? _editItems
                    : new List<CatalogItem> { _draftItem };

                string newPartNumber = TxtPartID.Text.Trim();
                string newTitle      = TxtTitle.Text.Trim();
                string newDesc       = TxtDesc.Text.Trim();
                string newMass       = TxtMass.Text.Trim();
                string newUoM        = TxtUoM.Text.Trim();
                string newBomType    = (CboBomType.SelectedItem is ComboBoxItem cbEdit) ? cbEdit.Content.ToString() : "HULL";

                foreach (var it in itemsToSave)
                {
                    it.PartNumber  = newPartNumber;
                    it.Title       = newTitle;
                    it.Description = newDesc;
                    it.Mass        = newMass;
                    it.UoM         = newUoM;
                    it.BomType     = newBomType;
                }

                try
                {
                    _masterService.MergeIntoMaster(itemsToSave.ToList());
                    string msg = itemsToSave.Count > 1
                        ? $"Saved {itemsToSave.Count} items successfully!"
                        : "Saved to Master Library successfully!";
                    MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.DialogResult = true;
                    this.Close();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
                return;
            }

            // Add mode: populate _draftItem rồi export/push block
            _draftItem.PartNumber  = TxtPartID.Text.Trim();
            _draftItem.Title       = TxtTitle.Text.Trim();
            _draftItem.Description = TxtDesc.Text.Trim();
            _draftItem.Mass        = TxtMass.Text.Trim();
            _draftItem.UoM         = TxtUoM.Text.Trim();
            _draftItem.BomType     = (CboBomType.SelectedItem is ComboBoxItem cb) ? cb.Content.ToString() : "HULL";

            if (_draftItem.EntityType == "Block" && !string.IsNullOrEmpty(_draftItem.BlockName))
            {
                // Add mode — Tier 2: export block ra .dwg để Insert hoạt động kể cả khi đổi drawing
                string filePath = Path.Combine(_masterService.MasterLibraryFolder, _draftItem.BlockName + ".dwg");
                _draftItem.FilePath = filePath;
                try
                {
                    var pushResult = _masterService.PushBlocksFromCurrentDrawing(new List<CatalogItem> { _draftItem });
                    if (pushResult.SuccessCount == 0)
                    {
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
