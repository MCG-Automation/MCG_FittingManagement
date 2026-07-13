using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Dialog nhỏ để sửa <see cref="CatalogItem.IsPlanView"/> và <see cref="CatalogItem.CountPlanViewOnly"/>
    /// cho item(s) đang chọn trong Item Library — không đụng tới các field Inventor-protected
    /// (PartNumber, Title, Description...) nên không cần chặn theo <c>Source</c> như "Edit Properties".
    /// Giá trị lưu vào project catalog; Hull harvest overlay từ đây (xem
    /// <c>FittingManagementService.OverlayViewTypeFromProjectCatalog</c>) chứ không đọc Master Catalog.
    /// </summary>
    public partial class EditViewTypeDialog : Window
    {
        private readonly IList<CatalogItem> _items;

        public EditViewTypeDialog(IList<CatalogItem> items)
        {
            InitializeComponent();
            _items = items;

            LblSummary.Text = _items.Count == 1
                ? $"View: {_items[0].BlockName}"
                : $"{_items.Count} view(s) selected";

            // Chỉ check nếu TẤT CẢ item đang chọn đều true — tránh hiển thị sai khi mixed state
            ChkIsPlanView.IsChecked = _items.All(i => i.IsPlanView);
            ChkCountPlanViewOnly.IsChecked = _items.All(i => i.CountPlanViewOnly);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            bool isPlanView = ChkIsPlanView.IsChecked == true;
            bool countPlanViewOnly = ChkCountPlanViewOnly.IsChecked == true;
            foreach (var item in _items)
            {
                item.IsPlanView = isPlanView;
                item.CountPlanViewOnly = countPlanViewOnly;
            }
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
