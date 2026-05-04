// TUYỆT ĐỐI KHÔNG using Autodesk.AutoCAD... ở đây theo quy tắc của CLAUDE.md
namespace MCGCadPlugin.Models.FittingManagement
{
    /// <summary>
    /// Catalog item dành cho Project Library. Kế thừa toàn bộ field của <see cref="CatalogItem"/>
    /// và phục vụ marker semantic — phân biệt rõ item thuộc project file với item của master.
    /// Field <c>ProjectPosNum</c> hiện vẫn đang ở base class để giữ JSON shape cũ tương thích
    /// (BOM Harvester đọc field này từ master catalog). Tương lai có thể nâng dần các field
    /// project-specific lên đây nếu cần.
    /// </summary>
    public class ProjectCatalogItem : CatalogItem
    {
    }
}
