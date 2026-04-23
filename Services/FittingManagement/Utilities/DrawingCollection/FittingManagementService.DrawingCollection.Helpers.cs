using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Drawing Collection — Static helpers: keep-as-is set, effective block name resolution,
    /// corrupt error classification, outlier detection, filename sanitization.
    /// </summary>
    public partial class FittingManagementService
    {
        #region Helpers

        /// <summary>
        /// Block exact-match cần giữ nguyên (không prefix tên file).
        /// IDW export title block có tên CHỨA "A1"/"A2"/"A3" (khổ giấy) — xử lý qua
        /// <see cref="TitleBlockSizeTokens"/> bên dưới, không nằm trong set này.
        /// </summary>
        private static readonly HashSet<string> ExactKeepAsIsBlocks =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CAS_HEAD" };

        /// <summary>
        /// Pattern substring cho title block từ IDW — block nào có tên CHỨA một trong các token
        /// (case-insensitive) sẽ được coi là khung tên và giữ nguyên không prefix.
        /// Ví dụ: "A1_Format", "MCG_A2_Title", "CAS_A3_Head" đều khớp.
        /// </summary>
        private static readonly string[] TitleBlockSizeTokens = { "A1", "A2", "A3" };

        /// <summary>True nếu block name khớp exact CAS_HEAD hoặc chứa A1/A2/A3 (khổ giấy).</summary>
        private static bool IsKeepAsIs(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (ExactKeepAsIsBlocks.Contains(name)) return true;
            return IsTitleBlockSizeName(name);
        }

        /// <summary>True nếu name CHỨA "A1" / "A2" / "A3" (case-insensitive) — pattern title block IDW.</summary>
        private static bool IsTitleBlockSizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string tok in TitleBlockSizeTokens)
            {
                if (name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Error status báo hiệu DWG bị lỗi nội bộ cần chạy AutoCAD RECOVER (eDwgNeedsRecovery, eFileSharingViolation...).
        /// User nên mở file thủ công chạy RECOVER rồi thử lại — không auto-recover trong plugin để tránh che giấu vấn đề.
        /// </summary>
        private static bool IsRecoverableCorruptError(Autodesk.AutoCAD.Runtime.Exception ex)
        {
            var s = ex.ErrorStatus;
            return s == Autodesk.AutoCAD.Runtime.ErrorStatus.DwgNeedsRecovery
                || s == Autodesk.AutoCAD.Runtime.ErrorStatus.DwkLockFileFound
                || s == Autodesk.AutoCAD.Runtime.ErrorStatus.FileAccessErr
                || s == Autodesk.AutoCAD.Runtime.ErrorStatus.FileSharingViolation;
        }

        /// <summary>
        /// Lấy "effective" block name cho BlockReference — nếu là dynamic block thì trả tên của AnonymousBlock gốc.
        /// Dynamic block reference có Name='*U123' (anonymous) nhưng DynamicBlockTableRecord trỏ về BTR gốc.
        /// </summary>
        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                if (br.IsDynamicBlock && !br.DynamicBlockTableRecord.IsNull)
                {
                    var btr = tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (btr != null) return btr.Name;
                }
                return br.Name;
            }
            catch { return br.Name; }
        }

        /// <summary>Thay các ký tự AutoCAD cấm trong block name bằng '_'.</summary>
        private static string SanitizeBlockNamePart(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Drawing";
            char[] invalid = { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`', '\'' };
            var chars = raw.Select(c => (invalid.Contains(c) || char.IsWhiteSpace(c)) ? '_' : c).ToArray();
            string clean = new string(chars).Trim('_');
            return string.IsNullOrEmpty(clean) ? "Drawing" : clean;
        }

        /// <summary>
        /// Tìm top-N entity có center xa median center nhất — candidate outlier kéo bbox.
        /// Dùng median thay vì mean để không bị chính outlier làm lệch tâm.
        /// </summary>
        private static List<EntityExtInfo> FindTopOutliers(List<EntityExtInfo> entities, int count)
        {
            if (entities == null || entities.Count == 0) return new List<EntityExtInfo>();

            var xs = entities.Select(e => e.CenterX).ToList();
            var ys = entities.Select(e => e.CenterY).ToList();
            double medX = Median(xs);
            double medY = Median(ys);

            return entities
                .OrderByDescending(e =>
                {
                    double dx = e.CenterX - medX;
                    double dy = e.CenterY - medY;
                    return dx * dx + dy * dy; // không cần Sqrt, rank không đổi
                })
                .Take(count)
                .ToList();
        }

        private static double Median(List<double> vals)
        {
            if (vals == null || vals.Count == 0) return 0;
            var sorted = vals.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return (sorted.Count % 2 == 0) ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
        }

        #endregion
    }
}
