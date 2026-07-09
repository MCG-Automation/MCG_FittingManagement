using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Dialog nhỏ để sửa <see cref="CatalogItem.ProjectPosNum"/> cho item(s) đang chọn trong Item Library.
    /// Thay cho việc edit trực tiếp trên Grid.
    /// </summary>
    public partial class EditPosNumDialog : Window
    {
        private readonly IList<CatalogItem> _items;

        public EditPosNumDialog(IList<CatalogItem> items)
        {
            InitializeComponent();
            _items = items;

            LblSummary.Text = _items.Count == 1
                ? $"Item: {_items[0].Title ?? _items[0].BlockName}"
                : $"{_items.Count} items selected";

            var distinctPos = _items.Select(i => i.ProjectPosNum).Distinct().ToList();
            TxtPosNum.Text = distinctPos.Count == 1 ? distinctPos[0] : "";
            TxtPosNum.Focus();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string newPos = TxtPosNum.Text.Trim();
            foreach (var item in _items) item.ProjectPosNum = newPos;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtPosNum_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnOk_Click(sender, e);
        }
    }
}
