using System.Collections.Generic;
// TUYỆT ĐỐI KHÔNG using Autodesk.AutoCAD... ở đây theo quy tắc của CLAUDE.md

namespace MCG_FittingManagement.Models.FittingManagement
{
    /// <summary>
    /// Chứa thông tin hình học của từng hình chiếu trích xuất từ Inventor.
    /// <c>Is3D</c> phân loại ortho (2D) vs iso/arbitrary (3D) — phía split-view skip view 3D
    /// để chỉ block hoá các hình chiếu 2D vào Master Library.
    /// </summary>
    public class ViewMetadata
    {
        public string Name { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary>true = view iso/arbitrary 3D (skip publish); false = view ortho 2D (publish vào Master).</summary>
        public bool Is3D { get; set; }
    }

    /// <summary>
    /// Cấu trúc file JSON thô được trích xuất từ công cụ Inventor Extractor.
    /// </summary>
    public class FittingMetadata
    {
        public string PartNumber { get; set; }
        public string Description { get; set; }
        public string Revision { get; set; }
        public string Mass { get; set; }
        public string Material { get; set; }
        public string Designer { get; set; } 
        public string Title { get; set; }
        public List<ViewMetadata> Views { get; set; } 
    }

    public class AccessoryItem
    {
        public string PartId { get; set; }
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Cấu trúc mục lục cho file MasterCatalog.json.
    /// </summary>
    public class CatalogItem
    {
        public string BlockName { get; set; }
        public string PartNumber { get; set; }
        public string Description { get; set; }
        public string Material { get; set; }
        public string Mass { get; set; }
        public string Revision { get; set; }
        public string Designer { get; set; }
        public string Title { get; set; }
        public string BomType { get; set; } 
        public string FilePath { get; set; }
        public string ProjectPosNum { get; set; } 
        public string EntityType { get; set; } = "Block";
        public string TriggerLayer { get; set; }
        public string TriggerColor { get; set; }
        public string UoM { get; set; } = "pcs";
        /// <summary>Nguồn gốc fitting: "Inventor" | "CAD-Block" | "CAD-Linear".</summary>
        public string Source { get; set; }
        public List<AccessoryItem> Accessories { get; set; } = new List<AccessoryItem>();
    }

    /// <summary>
    /// Đại diện cho 1 dòng dữ liệu thu hoạch được từ bản vẽ (BOM Harvest).
    /// </summary>
    public class BomHarvestRecord
    {
        public string PanelName { get; set; }     
        public string VaultName { get; set; }     
        public string PartId { get; set; }        
        public string XClass { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public string UoM { get; set; } = "pcs";
        public string ParentBlockName { get; set; }
        public bool IsAccessory { get; set; } = false;
        public string ParentPartId { get; set; } = "";
        public string Position { get; set; } 
        public string ProjectPosNum { get; set; } 

        // SỬA ĐỔI QUAN TRỌNG ĐỂ ĐÁP ỨNG KIẾN TRÚC MỚI: 
        // Thay vì dùng ObjectId của AutoCAD, ta dùng long (Handle Value)
        // để Model vẫn thuần .NET, không dính dáng đến dll của AutoCAD.
        public List<long> InstanceHandles { get; set; } = new List<long>();
    }
}