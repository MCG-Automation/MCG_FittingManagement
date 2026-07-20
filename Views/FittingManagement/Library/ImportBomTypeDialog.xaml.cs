using System.Windows;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Dialog nhỏ hỏi Target BOM Type (Equipment/Hull) khi bấm "Add from Inventor" trong cửa sổ Fitting
    /// Table — thay cho radio button trước đây nằm ở tab Palette. Sau khi OK, caller sẽ mở tiếp
    /// OpenFileDialog chọn file .idw.
    /// </summary>
    public partial class ImportBomTypeDialog : Window
    {
        /// <summary>"EQUIPMENT" hoặc "HULL" — chỉ hợp lệ khi DialogResult == true.</summary>
        public string BomType { get; private set; }

        public ImportBomTypeDialog()
        {
            InitializeComponent();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            BomType = (RadioHull.IsChecked == true) ? "HULL" : "EQUIPMENT";
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
