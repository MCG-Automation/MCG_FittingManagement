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

        /// <summary>
        /// true = view Plan/Top (nhìn từ trên xuống, trục Z chiếm ưu thế trong hướng camera).
        /// Dùng để phân biệt view nào là "view chính" khi 1 fitting có nhiều mặt cắt/view trong
        /// cùng 1 khung A1 (Hull BOM) — vd fitting loại "Guide" chỉ đếm Qty theo Plan View.
        /// </summary>
        public bool IsPlanView { get; set; }
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
        /// <summary>Ngày khởi tạo fitting (lúc user Import/Add from CAD). Format "yyyy-MM-dd HH:mm".</summary>
        public string CreatedDate { get; set; }
        public string TriggerLayer { get; set; }
        public string TriggerColor { get; set; }
        public string UoM { get; set; } = "pcs";
        /// <summary>Nguồn gốc fitting: "Inventor" | "CAD-Block" | "CAD-Linear".</summary>
        public string Source { get; set; }
        /// <summary>true nếu block này là view Plan/Top của fitting — xem <see cref="ViewMetadata.IsPlanView"/>.</summary>
        public bool IsPlanView { get; set; }
        /// <summary>
        /// true = fitting này (mọi view chia sẻ cùng PartNumber) chỉ đếm Qty theo view được đánh dấu
        /// <see cref="IsPlanView"/>, bỏ qua các view khác — dùng cho fitting loại "chỉ có Plan View là
        /// đại diện đúng số lượng thật" (vd Guide). Field tường minh, user tự đánh dấu qua Edit View Type
        /// — không hardcode theo tên/Title để dễ mở rộng cho fitting khác trong tương lai.
        /// </summary>
        public bool CountPlanViewOnly { get; set; }
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
        /// <summary>true nếu block/view nguồn của record này là Plan View — xem <see cref="CatalogItem.IsPlanView"/>.</summary>
        public bool IsPlanView { get; set; }
        /// <summary>true nếu fitting này chỉ đếm Qty theo Plan View — xem <see cref="CatalogItem.CountPlanViewOnly"/>.</summary>
        public bool CountPlanViewOnly { get; set; }

        // SỬA ĐỔI QUAN TRỌNG ĐỂ ĐÁP ỨNG KIẾN TRÚC MỚI: 
        // Thay vì dùng ObjectId của AutoCAD, ta dùng long (Handle Value)
        // để Model vẫn thuần .NET, không dính dáng đến dll của AutoCAD.
        public List<long> InstanceHandles { get; set; } = new List<long>();
    }
}