using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace MCG_FittingManagement.Services.FittingManagement
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

            // Layout (Hướng A — A1-frame anchored):
            //   EffectiveWidth = A1.Width nếu HasA1, ngược lại fallback Extents.Width.
            //   Advanced       = true nếu offsetX đã được dời (file rỗng + không A1 → false).
            public double EffectiveWidth;
            public bool Advanced;

            // Hướng A (N=1) — A1-aware layout anchor.
            //   HasA1            = ≥1 BlockReference A1/CAS_HEAD trong source MS có bbox hợp lệ.
            //   ValidA1Count     = tổng A1 hợp lệ trong file (>1 → MULTIPLE A1 warning, dùng leftmost làm anchor).
            //   A1MinXSrc/MaxXSrc= bbox X-range của A1 TRÁI NHẤT (chosen anchor, không phải envelope).
            //   LeftOverflowSrc  = max(0, A1MinXSrc − Extents.MinX) — entity nằm BÊN TRÁI A1 anchor.
            //   RightOverflowSrc = max(0, Extents.MaxX − A1MaxXSrc) — entity nằm BÊN PHẢI A1 anchor (gồm cả A1 phụ).
            public bool HasA1;
            public int ValidA1Count;
            public double A1MinXSrc;
            public double A1MaxXSrc;
            public double LeftOverflowSrc;
            public double RightOverflowSrc;
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

            // P3 — Anonymous block rename: tránh silent-drop khi dest template có anon blocks trùng tên.
            // `AnonRenamed` = anon block được MS-referenced + đổi tên thành công.
            // `AnonSkipped_Unreferenced` = anon block KHÔNG referenced từ MS → để nguyên (sẽ bị purge).
            public int AnonRenamed;
            public int AnonSkipped_Unreferenced;
            public int AnonFailed;
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

            // Type breakdown ModelSpace — count theo GetType().Name, tách with-extents vs no-extents.
            // Dùng để user verify DWG content và diagnose silent-drop ở Phase 2.
            public Dictionary<string, int> TypeCountWithExtents = new Dictionary<string, int>();
            public Dictionary<string, int> TypeCountNoExtents = new Dictionary<string, int>();

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
            public int PrimaryNotCloned; // IsPrimary && !IsCloned — entity BỊ DROP (silent-drop marker)
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
