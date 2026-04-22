using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Drawing Collection — Nested stats types dùng xuyên Phase 1, Phase 2, Summary.
    /// Tất cả private, không expose ra ngoài partial class container.
    /// </summary>
    public partial class FittingManagementService
    {
        #region Nested stats types

        /// <summary>1 file đã preprocess xong Phase 1, sẵn sàng cho Phase 2 clone.</summary>
        private class PreparedDrawing
        {
            public string FileName;
            public Database SideDb;
            public bool HasExtents;
            public double Width;
            public Extents3d Extents;

            // Stats gắn vào file để tổng kết ở summary cuối run.
            public RenameStats RenameStats;
            public PurgeStats PurgeStats;
            public ExtentsStats ExtStats;
            public CloneStats CloneStats;
            public double? PlacedOffsetX; // null nếu file fail ở phase 2

            // Layout sau khi apply A1-aware gap:
            //   EffectiveWidth = max(content_width, A1_rightmost_relative_to_offsetX).
            //   Advanced       = true nếu offsetX đã được dời (file rỗng + không A1 → false).
            public double EffectiveWidth;
            public bool Advanced;
        }

        /// <summary>Breakdown kết quả rename trên BlockTable của 1 side db.</summary>
        private class RenameStats
        {
            public int TotalBtr;
            public int Renamed;
            public int KeepAsIs_A1;
            public int KeepAsIs_CasHead;
            public int Skipped_Layout;
            public int Skipped_Xref;
            public int Skipped_Anonymous;
            public int Skipped_Conflict; // candidate name đã tồn tại
            public int Skipped_Empty;    // name rỗng hoặc '*' (system)
            public int Failed;
        }

        /// <summary>Stats purge recursive trên 1 side db.</summary>
        private class PurgeStats
        {
            public int TotalErased;
            public int Passes;
            public bool HitMaxPasses; // đạt ngưỡng 10 pass mà vẫn còn erase
        }

        /// <summary>Stats bbox + entity info Model Space của 1 side db.</summary>
        private class ExtentsStats
        {
            public bool HasExtents;
            public double Width;
            public double Height;
            public Extents3d Extents;
            public int TotalEntities;
            public int EntitiesWithExtents;
            public int EntitiesNoExtents;

            // Per-entity info — chỉ dùng cho diagnose outlier khi bbox vượt ngưỡng.
            // Không expose ObjectId vì side db bị dispose sau phase 1.
            public List<EntityExtInfo> EntityInfos = new List<EntityExtInfo>();

            // BlockReference có effective name ∈ KeepAsIsBlocks nằm TRỰC TIẾP trong Model Space nguồn.
            // (Không scan Paper Space/Layout — vì phase 2 không clone chúng.)
            public List<KeepAsIsRefInfo> KeepAsIsRefsInSource = new List<KeepAsIsRefInfo>();
        }

        /// <summary>Thông tin 1 entity trong Model Space — dùng cho outlier detection.</summary>
        private class EntityExtInfo
        {
            public string TypeName;
            public string HandleHex;    // "7F" giữ dạng Handle.ToString()
            public long HandleValue;    // để format hex chuẩn qua X formatter
            public double CenterX, CenterY;
            public double Width, Height;
            public string Layer;
        }

        /// <summary>Thông tin BlockReference keep-as-is trong SOURCE Model Space.</summary>
        private class KeepAsIsRefInfo
        {
            public string BlockName;
            public Point3d Position;
            public double Rotation;
            public Extents3d Bbox;
            public long HandleValue;
        }

        /// <summary>Thông tin BlockReference keep-as-is đã clone vào DEST (sau TransformBy).</summary>
        private class KeepAsIsClonedInfo
        {
            public string BlockName;
            public Point3d Position;
            public double Rotation;
            public Extents3d Bbox;
            public long DestHandleValue;
        }

        /// <summary>1 cặp overlap giữa 2 Keep-as-is reference trong dest.</summary>
        private class KeepAsIsOverlapPair
        {
            public int IndexA;
            public int IndexB;
            public double OverlapArea;
            public double PctOfSmaller; // overlap area / area của block nhỏ hơn × 100
        }

        /// <summary>Stats Phase 2 clone của 1 file.</summary>
        private class CloneStats
        {
            public int SrcIds;           // số entity modelspace source
            public int PrimaryCloned;    // IsPrimary && IsCloned — entity thực sự copy
            public int SymbolsCloned;    // !IsPrimary && IsCloned — symbol table record (layer/block/linetype) mới
            public int SymbolsIgnored;   // !IsPrimary && !IsCloned — trùng tên, dùng bản dest
            public int Transformed;      // TransformBy thành công
            public int TransformFailed;
            public double Dx;            // vector dịch X đã áp dụng

            // Keep-as-is BlockReference (A1/CAS_HEAD) đã clone + transform thành công vào dest.
            public List<KeepAsIsClonedInfo> KeepAsIsCloned = new List<KeepAsIsClonedInfo>();
        }

        #endregion
    }
}
