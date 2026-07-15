using System.Windows;
using System.Windows.Input;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Dialog nhỏ hỏi số bắt đầu (mặc định "1") cho chức năng Auto-Assign Pos — thay vì luôn hardcode
    /// bắt đầu từ "001" như trước.
    /// </summary>
    public partial class AutoAssignStartDialog : Window
    {
        /// <summary>Số bắt đầu user đã nhập — chỉ hợp lệ khi DialogResult == true.</summary>
        public int StartNumber { get; private set; }

        public AutoAssignStartDialog()
        {
            InitializeComponent();
            TxtStartNumber.Focus();
            TxtStartNumber.SelectAll();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtStartNumber.Text.Trim(), out int value) || value < 1)
            {
                MessageBox.Show("Enter a whole number ≥ 1.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            StartNumber = value;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtStartNumber_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnOk_Click(sender, e);
        }
    }
}
