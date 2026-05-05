using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// IDW Import — phần trích xuất iProperties (Part Number/Description/Material/Mass/Revision/Designer/Title)
    /// từ Inventor document, kèm helper an toàn đọc property và format Mass.
    /// </summary>
    public partial class FittingManagementService
    {
        /// <summary>
        /// Trích xuất iProperties từ document (ưu tiên model IPT/IAM, fallback drawing).
        /// Áp dụng FormatAndRoundMass cho Mass để ra dạng "24 kg" thay vì "24.532".
        /// </summary>
        private FittingMetadata ExtractIProperties(dynamic doc)
        {
            var metadata = new FittingMetadata();

            try
            {
                dynamic propSets = doc.PropertySets;

                // Design Tracking Properties — chứa Part Number, Description, Material, Mass, Designer
                dynamic designProps = propSets["Design Tracking Properties"];
                metadata.PartNumber = SafeGetProperty(designProps, "Part Number");
                metadata.Description = SafeGetProperty(designProps, "Description");
                metadata.Material = SafeGetProperty(designProps, "Material");
                metadata.Designer = SafeGetProperty(designProps, "Designer");

                // Mass — làm tròn + giữ unit suffix (24.532 kg → 25 kg)
                string rawMass = SafeGetProperty(designProps, "Mass");
                metadata.Mass = FormatAndRoundMass(rawMass);

                // Inventor Summary Information — Title, Revision, (fallback Author cho Designer)
                dynamic summaryProps = propSets["Inventor Summary Information"];
                metadata.Title = SafeGetProperty(summaryProps, "Title");
                metadata.Revision = SafeGetProperty(summaryProps, "Revision Number");

                // Fallback: nếu Design Tracking Properties không có Revision, thử lại trên design
                if (string.IsNullOrWhiteSpace(metadata.Revision))
                    metadata.Revision = SafeGetProperty(designProps, "Revision Number");

                // Fallback: nếu Designer rỗng, dùng Author
                if (string.IsNullOrWhiteSpace(metadata.Designer))
                    metadata.Designer = SafeGetProperty(summaryProps, "Author");
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "đọc iProperties", ex);
            }

            FileLogger.Log(LOG_PREFIX,
                $"  iProperties: PartNumber='{metadata.PartNumber}', Title='{metadata.Title}', Mass='{metadata.Mass}'");
            return metadata;
        }

        /// <summary>
        /// Đọc giá trị property an toàn — trả về chuỗi rỗng nếu không tồn tại.
        /// Giữ raw string cho double (không F3); caller tự format (ví dụ Mass).
        /// </summary>
        private string SafeGetProperty(dynamic propSet, string propName)
        {
            try
            {
                dynamic prop = propSet[propName];
                object val = prop.Value;
                if (val == null) return "";

                if (val is double dVal)
                    return dVal.ToString(CultureInfo.InvariantCulture);
                return val.ToString();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Parse raw Mass từ Inventor (ví dụ "24.532 kg") → làm tròn số → "25 kg".
        /// Giữ nguyên unit suffix nếu có; fallback return raw nếu parse fail.
        /// </summary>
        private static string FormatAndRoundMass(string rawMass)
        {
            if (string.IsNullOrWhiteSpace(rawMass)) return "";

            try
            {
                Match match = Regex.Match(rawMass.Trim(), @"^([\d\.,]+)\s*(.*)$");
                if (match.Success)
                {
                    string numberPart = match.Groups[1].Value.Replace(",", ".");
                    string unitPart = match.Groups[2].Value.Trim();

                    if (double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                    {
                        double rounded = Math.Round(parsed, 0);
                        return string.IsNullOrEmpty(unitPart)
                            ? rounded.ToString(CultureInfo.InvariantCulture)
                            : $"{rounded.ToString(CultureInfo.InvariantCulture)} {unitPart}";
                    }
                }
                return rawMass;
            }
            catch
            {
                return rawMass;
            }
        }
    }
}
