using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media; // Color, SolidColorBrush

namespace MCG_FittingManagement.Views.FittingManagement
{
    public partial class BlockRenameDialog : Window
    {
        public string OldPattern { get; private set; }
        public string NewPattern { get; private set; }
        public bool IsRenameCreateNew { get; private set; }

        private readonly List<BlockNameItem> _items;

        public BlockRenameDialog(IEnumerable<string> blockNames)
        {
            InitializeComponent();

            _items = blockNames.OrderBy(n => n)
                               .Select(n => new BlockNameItem { Name = n })
                               .ToList();

            LblBlockCount.Text = $"Scanned blocks ({_items.Count}):";
            LstBlocks.ItemsSource = _items;
            UpdateOkButton(0);

            if (_items.Count == 1)
                LstBlocks.SelectedIndex = 0;
        }

        // Click item ở Textbox 1 → fill Textbox 2 và Textbox 3
        private void LstBlocks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(LstBlocks.SelectedItem is BlockNameItem item)) return;

            TxtOldName.Text = item.Name;
            TxtNewName.Text = $"{item.Name}_New";
            TxtNewName.Focus();
            TxtNewName.SelectAll();
        }

        // Textbox 2 thay đổi → cập nhật highlight danh sách, match count, trạng thái nút OK
        private void TxtOldName_TextChanged(object sender, TextChangedEventArgs e)
        {
            string pattern = TxtOldName.Text.Trim();

            // Validate: tối đa 1 dấu *
            int starCount = pattern.Count(c => c == '*');
            if (starCount > 1)
            {
                LblMatchCount.Text = "max 1 '*'";
                LblMatchCount.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
                foreach (var item in _items) item.IsMatch = false;
                UpdateOkButton(0);
                return;
            }

            int matchCount = 0;
            foreach (var item in _items)
            {
                item.IsMatch = MatchesPattern(item.Name, pattern);
                if (item.IsMatch) matchCount++;
            }

            if (string.IsNullOrEmpty(pattern))
            {
                LblMatchCount.Text = string.Empty;
            }
            else
            {
                LblMatchCount.Text = matchCount > 0 ? $"{matchCount} match(es)" : "no match";
                LblMatchCount.Foreground = matchCount > 0
                    ? new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10))  // #107C10 — green
                    : new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)); // #C42B1C — red
            }

            UpdateOkButton(matchCount);
        }

        /// <summary>
        /// Kiểm tra tên block có khớp với pattern không.
        /// Pattern không có * → so sánh exact (OrdinalIgnoreCase).
        /// Pattern có 1 * → prefix + suffix match.
        /// </summary>
        internal static bool MatchesPattern(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (!pattern.Contains("*"))
                return name.Equals(pattern, System.StringComparison.OrdinalIgnoreCase);

            int starIdx = pattern.IndexOf('*');
            string prefix = pattern.Substring(0, starIdx);
            string suffix = pattern.Substring(starIdx + 1);

            return name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)
                && name.Length >= prefix.Length + suffix.Length;
        }

        private void UpdateOkButton(int matchCount)
        {
            BtnOk.IsEnabled = matchCount > 0;
            BtnOk.Content   = matchCount > 0 ? $"Rename ({matchCount})" : "Rename (0)";
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string oldPattern = TxtOldName.Text.Trim();
            string newPattern = TxtNewName.Text.Trim();

            if (string.IsNullOrEmpty(oldPattern))
            {
                MessageBox.Show("Vui lòng nhập Old name.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(newPattern))
            {
                MessageBox.Show("Vui lòng nhập New name.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Nếu old có * thì new cũng phải có * để tránh conflict
            if (oldPattern.Contains("*") && !newPattern.Contains("*"))
            {
                MessageBox.Show("New name phải chứa '*' khi Old name có wildcard.",
                    "Thiếu wildcard", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OldPattern        = oldPattern;
            NewPattern        = newPattern;
            IsRenameCreateNew = RbRenameCreateNew.IsChecked == true;
            DialogResult      = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TxtNewName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnOk_Click(sender, null);
        }
    }

    /// <summary>
    /// Item model cho ListBox trong BlockRenameDialog.
    /// IsMatch được cập nhật realtime khi user gõ pattern ở Textbox 2.
    /// </summary>
    internal class BlockNameItem : INotifyPropertyChanged
    {
        private bool _isMatch;

        public string Name { get; set; }

        public bool IsMatch
        {
            get => _isMatch;
            set
            {
                if (_isMatch == value) return;
                _isMatch = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMatch)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
