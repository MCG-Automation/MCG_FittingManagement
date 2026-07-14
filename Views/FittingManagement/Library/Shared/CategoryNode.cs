using System.Collections.Generic;
using System.ComponentModel;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Node hiển thị trong TreeView của Library window — group theo BomType → PartNumber.
    /// Dùng trong FittingTableWindow.
    /// </summary>
    public class CategoryNode : INotifyPropertyChanged
    {
        public string CategoryName { get; set; }
        public string CountLabel { get; set; }
        public List<CategoryNode> Children { get; set; } = new List<CategoryNode>();
        public List<CatalogItem> Items { get; set; } = new List<CatalogItem>();

        /// <summary>0 = root ("All Fittings"/"Recently"); 1 = BomType category ("Fitting In
        /// Equipment"/"Fitting In Hull"); 2 = Sub Folder (1 PartNumber duy nhất) — chỉ Level 2 mới cho
        /// phép kéo-thả sắp xếp lại (xem FittingTableWindow.xaml.cs).</summary>
        public int Level { get; set; }

        /// <summary>Node cha — null nếu Level 0/1. Dùng để giới hạn kéo-thả chỉ trong cùng 1 BomType
        /// category (không cho kéo chéo giữa Equipment/Hull).</summary>
        public CategoryNode Parent { get; set; }

        private bool _isMultiSelected;
        /// <summary>true khi node đang nằm trong tập multi-select (Ctrl/Shift-click) — dùng để tô nền
        /// TreeViewItem qua DataTrigger trong XAML (xem FittingTableWindow.xaml).</summary>
        public bool IsMultiSelected
        {
            get => _isMultiSelected;
            set
            {
                if (_isMultiSelected == value) return;
                _isMultiSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMultiSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
