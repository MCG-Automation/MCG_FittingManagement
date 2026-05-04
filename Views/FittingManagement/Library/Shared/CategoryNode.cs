using System.Collections.Generic;
using MCGCadPlugin.Models.FittingManagement;

namespace MCGCadPlugin.Views.FittingManagement
{
    /// <summary>
    /// Node hiển thị trong TreeView của Library window — group theo BomType → Title.
    /// Dùng chung cho MasterLibraryWindow và ProjectLibraryWindow.
    /// </summary>
    public class CategoryNode
    {
        public string CategoryName { get; set; }
        public string CountLabel { get; set; }
        public List<CategoryNode> Children { get; set; } = new List<CategoryNode>();
        public List<CatalogItem> Items { get; set; } = new List<CatalogItem>();
    }
}
