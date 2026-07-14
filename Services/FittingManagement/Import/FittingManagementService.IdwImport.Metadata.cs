using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// IDW Import — phần trích xuất iProperties (Part Number/Description/Material/Mass/Revision/Designer/Title)
    /// từ Inventor document, kèm helper an toàn đọc property và format Mass. Ngoài 7 field "chính" này,
    /// còn duyệt TOÀN BỘ PropertySet khác (bao gồm "User Defined Properties" — nơi Vault UDP thường map
    /// vào) để gom vào <see cref="FittingMetadata.ExtraProperties"/>, phục vụ Customize Grid ở Fitting Table.
    /// </summary>
    public partial class FittingManagementService
    {
        /// <summary>Tên các field đã trích riêng ở trên — bỏ qua khi duyệt generic để tránh trùng cột.</summary>
        private static readonly HashSet<string> KNOWN_IPROPERTY_NAMES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Part Number", "Description", "Material", "Designer", "Mass", "Title", "Revision Number", "Author"
        };

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

                // Duyệt TOÀN BỘ PropertySet (Design Tracking, Summary, Document Summary, User Defined
                // Properties — nơi Vault UDP thường map vào...) để gom mọi field CÒN LẠI vào
                // ExtraProperties, phục vụ Customize Grid ở Fitting Table (xem yêu cầu user). Field đã
                // trích riêng ở trên (KNOWN_IPROPERTY_NAMES) bị bỏ qua để tránh hiện trùng 2 cột.
                ExtractAllPropertiesGeneric(propSets, metadata.ExtraProperties);
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "đọc iProperties", ex);
            }

            FileLogger.Log(LOG_PREFIX,
                $"  iProperties: PartNumber='{metadata.PartNumber}', Title='{metadata.Title}', Mass='{metadata.Mass}', ExtraProperties={metadata.ExtraProperties.Count}");
            return metadata;
        }

        /// <summary>
        /// Duyệt mọi PropertySet + mọi Property bên trong (COM collection — enumerable qua dynamic/foreach)
        /// gom vào <paramref name="bag"/>. Mỗi property đọc riêng trong try/catch — 1 property lỗi (vd cần
        /// prompt, hoặc kiểu dữ liệu lạ) không được làm hỏng cả vòng lặp. Field trùng tên giữa các
        /// PropertySet khác nhau: giữ giá trị ĐẦU TIÊN gặp (bỏ qua nếu key đã có trong bag).
        /// </summary>
        private void ExtractAllPropertiesGeneric(dynamic propSets, Dictionary<string, string> bag)
        {
            try
            {
                foreach (dynamic propSet in propSets)
                {
                    try
                    {
                        foreach (dynamic prop in propSet)
                        {
                            try
                            {
                                string name = prop.DisplayName ?? prop.PropertyName ?? "";
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                if (KNOWN_IPROPERTY_NAMES.Contains(name)) continue;
                                if (bag.ContainsKey(name)) continue; // giữ giá trị đầu tiên gặp

                                object val = prop.Value;
                                string strVal = val == null ? "" : (val is double dVal ? dVal.ToString(CultureInfo.InvariantCulture) : val.ToString());
                                bag[name] = strVal;
                            }
                            catch
                            {
                                // 1 property lỗi (vd cần prompt/kiểu dữ liệu lạ) — bỏ qua, không hỏng cả vòng lặp
                            }
                        }
                    }
                    catch
                    {
                        // 1 PropertySet lỗi (hiếm) — bỏ qua, thử PropertySet tiếp theo
                    }
                }
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "duyệt generic PropertySets", ex);
            }
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
