using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Xử lý trích xuất geometry và metadata từ file IDW của Inventor qua COM Interop.
    /// Kết quả: file DWG + JSON được lưu vào thư mục thư viện.
    /// </summary>
    public partial class FittingManagementService
    {
        /// <summary>
        /// Gói dữ liệu của 1 file IDW đã extract thành công, dùng làm input cho bước split-view.
        /// </summary>
        private class ExtractedIdw
        {
            public string SourceIdwName { get; set; }
            public string DwgPath { get; set; }
            public FittingMetadata Metadata { get; set; }
        }

        /// <summary>
        /// Luồng gộp async: Phase 1 (Inventor COM — chậm) chạy trên worker thread qua <see cref="Task.Run(Action)"/>
        /// để UI AutoCAD không bị khoá; Phase 2 (AutoCAD db — cần main thread) chạy lại trên thread gốc
        /// của caller sau `await`. Báo tiến độ qua <paramref name="progress"/> nếu có.
        /// </summary>
        /// <param name="idwPaths">Danh sách đường dẫn file .idw</param>
        /// <param name="bomType">"PANEL" hoặc "DETAIL"</param>
        /// <param name="progress">Callback nhận thông điệp trạng thái (có thể null).</param>
        /// <returns>ImportResult tính theo file IDW (thành công khi extract OK và tạo được ≥1 block)</returns>
        public async Task<ImportResult> ImportIdwFilesAsync(string[] idwPaths, string bomType, bool pullFromVault = false, IProgress<string> progress = null)
        {
            var result = new ImportResult();
            FileLogger.LogSessionStart($"ImportIdwFilesAsync ({idwPaths.Length} files, BomType={bomType}, pullFromVault={pullFromVault})");
            FileLogger.Log(LOG_PREFIX, $"Bắt đầu ImportIdwFilesAsync — {idwPaths.Length} file(s), BomType={bomType}, pullFromVault={pullFromVault}...");
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu ImportIdwFilesAsync — {idwPaths.Length} file(s), BomType={bomType}, pullFromVault={pullFromVault}...");

            // PHASE 1 — worker thread: Inventor COM (Open/SaveAs/Close — chậm)
            progress?.Report($"Extracting {idwPaths.Length} IDW file(s) via Inventor...");
            var extractedItems = await Task.Run(() => ExtractAllIdw(idwPaths, result, progress, pullFromVault));

            // PHASE 2 — thread gốc: AutoCAD db phải chạy trên main thread
            if (extractedItems.Count > 0)
            {
                progress?.Report($"Creating blocks from {extractedItems.Count} DWG(s)...");
                CreateBlocksFromExtracted(extractedItems, bomType, result, progress);
            }

            progress?.Report($"Done. Success={result.SuccessCount}, Failed={result.FailCount}");
            FileLogger.Log(LOG_PREFIX, $"HOÀN TẤT ImportIdwFilesAsync — Thành công: {result.SuccessCount}, Thất bại: {result.FailCount}.");
            Debug.WriteLine($"{LOG_PREFIX} HOÀN TẤT ImportIdwFilesAsync — Thành công: {result.SuccessCount}, Thất bại: {result.FailCount}.");
            return result;
        }

        /// <summary>
        /// PHASE 1 — Extract toàn bộ IDW. File extract fail bị ghi vào result.Errors ngay;
        /// file extract OK được trả về để PHASE 2 dùng. Chạy trên worker thread.
        /// Nếu <paramref name="pullFromVault"/> = true, mỗi file sẽ được refresh từ Vault trước khi extract.
        /// </summary>
        private List<ExtractedIdw> ExtractAllIdw(string[] idwPaths, ImportResult result, IProgress<string> progress, bool pullFromVault)
        {
            var extracted = new List<ExtractedIdw>();
            bool weStartedInventor = false;
            dynamic invApp = null;

            try
            {
                invApp = AcquireInventorInstance(out weStartedInventor);

                if (!Directory.Exists(_libraryFolderPath))
                {
                    Directory.CreateDirectory(_libraryFolderPath);
                    FileLogger.Log(LOG_PREFIX, $"Đã tạo thư mục: {_libraryFolderPath}");
                }

                int total = idwPaths.Length;
                for (int i = 0; i < total; i++)
                {
                    string idwPath = idwPaths[i];
                    string fileName = Path.GetFileName(idwPath);

                    // Optional: pull latest từ Vault TRƯỚC khi mở file để đảm bảo extract từ disk là version mới nhất.
                    // Không fail toàn file nếu Vault không available — graceful degradation, proceed với file local.
                    if (pullFromVault)
                    {
                        progress?.Report($"[{i + 1}/{total}] Vault refresh: {fileName}");
                        Models.FittingManagement.VaultRefreshResult vaultResult = null;
                        try
                        {
                            vaultResult = TryPullLatestFromVault(invApp, idwPath);
                            if (vaultResult.Status == Models.FittingManagement.VaultRefreshStatus.Failed)
                            {
                                FileLogger.Log(LOG_PREFIX, $"  Vault refresh fail (non-fatal) cho '{fileName}': {vaultResult.Message}");
                            }
                        }
                        catch (System.Exception vex)
                        {
                            FileLogger.LogException(LOG_PREFIX, $"TryPullLatestFromVault '{fileName}' (non-fatal)", vex);
                            vaultResult = Models.FittingManagement.VaultRefreshResult.Failed(idwPath, vex.Message);
                        }

                        // Bổ sung FilePath nếu result chưa có (SkippedNoAddIn/NotLoggedIn không có path).
                        if (string.IsNullOrEmpty(vaultResult.FilePath))
                            vaultResult.FilePath = idwPath;

                        result.VaultResults.Add(vaultResult);

                        // Realtime progress với status icon để user thấy ngay kết quả Vault từng file.
                        string icon = vaultResult.IsSuccess ? "✓" :
                                      vaultResult.Status == Models.FittingManagement.VaultRefreshStatus.Failed ? "✗" : "⚠";
                        string statusLabel = StatusShortLabel(vaultResult.Status);
                        progress?.Report($"[{i + 1}/{total}] {icon} Vault {statusLabel}: {fileName}");
                    }

                    progress?.Report($"[{i + 1}/{total}] Extracting: {fileName}");

                    try
                    {
                        ExtractedIdw item = ProcessSingleIdwFile(invApp, idwPath);
                        extracted.Add(item);
                        FileLogger.Log(LOG_PREFIX, $"Extract IDW OK: {fileName} → {item.Metadata.Views?.Count ?? 0} view(s)");
                    }
                    catch (System.Exception ex)
                    {
                        result.FailCount++;
                        FileLogger.LogException(LOG_PREFIX, $"extract IDW '{fileName}'", ex);
                        result.AddError(fileName, $"Extract: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "ExtractAllIdw (outer)", ex);
                throw;
            }
            finally
            {
                ReleaseInventorInstance(invApp, weStartedInventor);
            }

            return extracted;
        }

        #region IDW Import — Private Helpers

        /// <summary>
        /// Kết nối hoặc khởi tạo Inventor COM Application.
        /// </summary>
        private dynamic AcquireInventorInstance(out bool weStarted)
        {
            weStarted = false;
            dynamic invApp;

            try
            {
                // Thử kết nối instance đang chạy
                invApp = Marshal.GetActiveObject("Inventor.Application");
                FileLogger.Log(LOG_PREFIX, "Đã kết nối Inventor instance đang chạy.");
            }
            catch
            {
                // Inventor chưa chạy — khởi tạo mới
                Type invType = Type.GetTypeFromProgID("Inventor.Application");
                if (invType == null)
                {
                    FileLogger.Log(LOG_PREFIX, "LỖI: Inventor chưa được cài đặt trên máy.");
                    throw new InvalidOperationException(
                        "Inventor chưa được cài đặt trên máy này. Vui lòng cài Inventor để sử dụng tính năng Import IDW.");
                }

                invApp = Activator.CreateInstance(invType);
                invApp.Visible = false;
                weStarted = true;
                FileLogger.Log(LOG_PREFIX, "Đã khởi tạo Inventor instance mới (background).");
            }

            // Giảm cơ hội Inventor popup dialog modal (huỷ warning/prompt khi open/save)
            // — dùng try/catch vì một vài build Inventor không expose property này.
            try { invApp.SilentOperation = true; }
            catch (System.Exception ex)
            {
                FileLogger.Log(LOG_PREFIX, $"SilentOperation không khả dụng: {ex.Message}");
            }

            return invApp;
        }

        /// <summary>
        /// Giải phóng Inventor COM objects an toàn.
        /// </summary>
        private void ReleaseInventorInstance(dynamic invApp, bool weStarted)
        {
            if (invApp == null) return;

            try
            {
                if (weStarted)
                {
                    invApp.Quit();
                    FileLogger.Log(LOG_PREFIX, "Đã tắt Inventor instance.");
                }
                Marshal.ReleaseComObject(invApp);
            }
            catch (COMException comEx) when (
                comEx.HResult == unchecked((int)0x80010114) ||    // "The requested object does not exist"
                comEx.HResult == unchecked((int)0x800706BA))       // "The RPC server is unavailable"
            {
                // Benign — Inventor.exe đã quit trước khi ta release được ref.
                FileLogger.Log(LOG_PREFIX, $"Inventor COM đã tự giải phóng (Inventor.exe đã thoát, benign 0x{comEx.HResult:X8}).");
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "giải phóng Inventor COM", ex);
            }
        }

        /// <summary>
        /// Xử lý 1 file IDW: trích xuất metadata, export DWG, lưu JSON,
        /// trả về gói <see cref="ExtractedIdw"/> để phase split-view dùng tiếp.
        /// </summary>
        private ExtractedIdw ProcessSingleIdwFile(dynamic invApp, string idwPath)
        {
            FileLogger.Log(LOG_PREFIX, $"Đang xử lý: {Path.GetFileName(idwPath)}...");

            string baseName = Path.GetFileNameWithoutExtension(idwPath);
            string dwgOutputPath = Path.Combine(_libraryFolderPath, baseName + ".dwg");
            string jsonOutputPath = Path.Combine(_libraryFolderPath, baseName + ".json");

            FittingMetadata metadata = null;
            dynamic drawingDoc = null;
            try
            {
                // Mở IDW background (OpenVisible=false — nhanh hơn, không render UI).
                // drawingDoc.SaveAs() vẫn export DWG được mà không cần view on-screen.
                FileLogger.Log(LOG_PREFIX, $"  Bước 1/4: Đang mở file IDW (OpenVisible=false)...");
                drawingDoc = invApp.Documents.Open(idwPath, false);

                // Trích xuất iProperties từ Referenced Model (IPT/IAM) — chỗ chứa PN/Mass thật.
                // Drawing iProperties thường rỗng — chỉ fallback khi không tìm thấy model ref.
                FileLogger.Log(LOG_PREFIX, $"  Bước 2/4: Đang trích xuất iProperties từ model...");
                dynamic modelDoc = GetReferencedModel(drawingDoc);
                if (modelDoc != null)
                    FileLogger.Log(LOG_PREFIX, "    Đã tìm thấy Referenced Model.");
                else
                    FileLogger.Log(LOG_PREFIX, "    CẢNH BÁO: Không tìm thấy Referenced Model — fallback đọc từ drawing.");
                metadata = ExtractIProperties(modelDoc ?? drawingDoc);

                // Trích xuất drawing views — áp dụng view scale để ra tọa độ model mm
                FileLogger.Log(LOG_PREFIX, $"  Bước 3/4: Đang trích xuất Drawing Views...");
                metadata.Views = ExtractDrawingViews(drawingDoc);

                // Export DWG
                FileLogger.Log(LOG_PREFIX, $"  Bước 4/4: Đang export DWG tới {dwgOutputPath}...");
                ExportIdwToDwg(invApp, drawingDoc, dwgOutputPath);

                // Lưu metadata ra JSON (audit trail + tương thích với external tool)
                string json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                File.WriteAllText(jsonOutputPath, json);
                FileLogger.Log(LOG_PREFIX, $"  Đã lưu JSON: {jsonOutputPath}");
            }
            finally
            {
                // Đóng document không lưu
                if (drawingDoc != null)
                {
                    try
                    {
                        drawingDoc.Close(true); // true = skip save
                        Marshal.ReleaseComObject(drawingDoc);
                    }
                    catch (COMException comEx) when (
                        comEx.HResult == unchecked((int)0x80010114) ||    // "The requested object does not exist"
                        comEx.HResult == unchecked((int)0x800706BA))       // "The RPC server is unavailable"
                    {
                        // Benign — Inventor tự unload document sau SaveAs; Close() gọi lên ref đã chết.
                        FileLogger.Log(LOG_PREFIX, $"  IDW document đã tự unload (benign COM 0x{comEx.HResult:X8}).");
                    }
                    catch (System.Exception ex)
                    {
                        FileLogger.LogException(LOG_PREFIX, "đóng IDW", ex);
                    }
                }
            }

            return new ExtractedIdw
            {
                SourceIdwName = Path.GetFileName(idwPath),
                DwgPath = dwgOutputPath,
                Metadata = metadata
            };
        }

        /// <summary>
        /// Duyệt các sheet/drawing view để tìm ReferencedDocument (IPT/IAM) đầu tiên.
        /// Trả về null nếu drawing không reference model nào (rare).
        /// </summary>
        private dynamic GetReferencedModel(dynamic drawingDoc)
        {
            try
            {
                foreach (dynamic sheet in drawingDoc.Sheets)
                {
                    foreach (dynamic view in sheet.DrawingViews)
                    {
                        try
                        {
                            dynamic descriptor = view.ReferencedDocumentDescriptor;
                            if (descriptor == null) continue;
                            dynamic refDoc = descriptor.ReferencedDocument;
                            if (refDoc != null) return refDoc;
                        }
                        catch { /* view không có ref doc — next */ }
                    }
                }
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "GetReferencedModel", ex);
            }
            return null;
        }

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

        /// <summary>
        /// Duyệt tất cả sheet và drawing view, convert tâm/kích thước từ tọa độ sheet (cm) sang tọa độ model (mm).
        /// Công thức: model_mm = sheet_cm × 10 ÷ view.Scale.
        /// View name dùng "View_N" tuần tự (tránh collision và ký tự không hợp lệ trong tên block).
        /// </summary>
        private List<ViewMetadata> ExtractDrawingViews(dynamic drawingDoc)
        {
            var views = new List<ViewMetadata>();
            int viewIndex = 1;

            try
            {
                foreach (dynamic sheet in drawingDoc.Sheets)
                {
                    // baseScaleFactor = 1 / view.Scale — dịch từ sheet-coords về model-coords
                    // (DWG export giữ geometry ở tỉ lệ model thật, nên metadata cũng phải ở model mm)
                    double baseScaleFactor = 1.0;
                    try
                    {
                        if (sheet.DrawingViews.Count > 0)
                        {
                            double firstScale = (double)sheet.DrawingViews[1].Scale;
                            if (firstScale > 0) baseScaleFactor = 1.0 / firstScale;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        FileLogger.Log(LOG_PREFIX, $"  CẢNH BÁO: không đọc được Scale, dùng 1.0: {ex.Message}");
                    }

                    foreach (dynamic drawingView in sheet.DrawingViews)
                    {
                        try
                        {
                            // DrawingView.Center là Point2d ở sheet coords (cm) — lấy *10 ra mm, rồi *baseScaleFactor ra model-mm
                            dynamic center = drawingView.Center;
                            double cxSheet = (double)center.X * 10.0;
                            double cySheet = (double)center.Y * 10.0;
                            double wSheet = (double)drawingView.Width * 10.0;
                            double hSheet = (double)drawingView.Height * 10.0;

                            views.Add(new ViewMetadata
                            {
                                Name = "View_" + viewIndex,
                                CenterX = cxSheet * baseScaleFactor,
                                CenterY = cySheet * baseScaleFactor,
                                Width = wSheet * baseScaleFactor,
                                Height = hSheet * baseScaleFactor
                            });
                            viewIndex++;
                        }
                        catch (System.Exception ex)
                        {
                            FileLogger.LogException(LOG_PREFIX, "đọc DrawingView", ex);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "duyệt Sheets", ex);
            }

            FileLogger.Log(LOG_PREFIX, $"  Tìm thấy {views.Count} drawing view(s) (tọa độ model mm).");
            return views;
        }

        /// <summary>
        /// Export file IDW sang định dạng DWG.
        /// Chiến lược: Thử Document.SaveAs() trước (đơn giản, Inventor tự detect format).
        /// Nếu fail, fallback sang DWG Translator với INI (nếu tìm được).
        /// </summary>
        private void ExportIdwToDwg(dynamic invApp, dynamic drawingDoc, string dwgOutputPath)
        {
            // Xóa file cũ nếu tồn tại (tránh Inventor warning dialog)
            if (File.Exists(dwgOutputPath))
            {
                FileLogger.Log(LOG_PREFIX, "    Đang xóa file DWG cũ...");
                try { File.Delete(dwgOutputPath); }
                catch (System.Exception ex)
                {
                    FileLogger.LogException(LOG_PREFIX, "xóa file DWG cũ", ex);
                }
            }

            // === CHIẾN LƯỢC 1: Document.SaveAs — đơn giản nhất, không cần INI ===
            FileLogger.Log(LOG_PREFIX, "    [A1] Thử export qua drawingDoc.SaveAs(path, true)...");
            try
            {
                drawingDoc.SaveAs(dwgOutputPath, true); // true = SaveCopyAs, giữ IDW gốc
                if (File.Exists(dwgOutputPath))
                {
                    FileLogger.Log(LOG_PREFIX, $"    [A2] Export DWG THÀNH CÔNG qua SaveAs: {dwgOutputPath}");
                    return;
                }
                FileLogger.Log(LOG_PREFIX, "    [A3] SaveAs hoàn tất nhưng file không được tạo — fallback sang translator.");
            }
            catch (System.Exception exSaveAs)
            {
                FileLogger.LogException(LOG_PREFIX, "SaveAs (chiến lược 1)", exSaveAs);
                FileLogger.Log(LOG_PREFIX, "    [A3] SaveAs fail — fallback sang translator.");
            }

            // === CHIẾN LƯỢC 2: DWG Translator + INI ===
            ExportViaTranslator(invApp, drawingDoc, dwgOutputPath);
        }

        /// <summary>
        /// Fallback: Export qua DWG Translator Add-In với file INI.
        /// </summary>
        private void ExportViaTranslator(dynamic invApp, dynamic drawingDoc, string dwgOutputPath)
        {
            const string DWG_TRANSLATOR_GUID = "{C24E3AC2-122E-11D5-8E91-0010B541CD80}";

            FileLogger.Log(LOG_PREFIX, "    [B1] Đang lấy DWG Translator Add-In...");
            dynamic translatorAddin;
            try
            {
                translatorAddin = invApp.ApplicationAddIns.ItemById(DWG_TRANSLATOR_GUID);
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "lấy DWG Translator Add-In", ex);
                throw new InvalidOperationException(
                    $"Không tìm thấy DWG Translator Add-In (GUID: {DWG_TRANSLATOR_GUID}).", ex);
            }
            if (translatorAddin == null)
                throw new InvalidOperationException("DWG Translator Add-In không tồn tại.");

            FileLogger.Log(LOG_PREFIX, "    [B2] Đang kích hoạt Add-In...");
            try { translatorAddin.Activate(); }
            catch (System.Exception ex)
            {
                FileLogger.Log(LOG_PREFIX, $"    Activate() warning: {ex.Message}");
            }

            dynamic transientObjects = invApp.TransientObjects;
            dynamic options = transientObjects.CreateNameValueMap();
            dynamic dataMedium = transientObjects.CreateDataMedium();
            dataMedium.FileName = dwgOutputPath;
            dynamic transContext = transientObjects.CreateTranslationContext();
            transContext.Type = 102657; // kFileBrowseIOMechanism

            // Tìm INI file — probe nhiều location kể cả INI của user tự tạo trong plugin folder
            string iniPath = FindInventorDwgIniPath();
            if (string.IsNullOrEmpty(iniPath))
            {
                // Không tìm được INI chuẩn — tạo INI tối thiểu trong %APPDATA%\MCGCadPlugin\
                iniPath = CreateMinimalDwgIni();
                FileLogger.Log(LOG_PREFIX, $"    [B3] Đã tạo INI tối thiểu: {iniPath}");
            }
            else
            {
                FileLogger.Log(LOG_PREFIX, $"    [B3] Tìm thấy DWG INI: {iniPath}");
            }

            if (!string.IsNullOrEmpty(iniPath))
            {
                try
                {
                    options.Add("Export_Acad_IniFile", iniPath);
                    FileLogger.Log(LOG_PREFIX, "    [B4] Đã set Export_Acad_IniFile.");
                }
                catch (System.Exception exAdd)
                {
                    FileLogger.Log(LOG_PREFIX, $"    [B4] Warning: {exAdd.Message}");
                }
            }

            FileLogger.Log(LOG_PREFIX, "    [B5] Đang gọi SaveCopyAs...");
            translatorAddin.SaveCopyAs(drawingDoc, transContext, options, dataMedium);
            FileLogger.Log(LOG_PREFIX, $"    [B6] Export DWG THÀNH CÔNG qua Translator: {dwgOutputPath}");
        }

        /// <summary>
        /// Tạo file INI tối thiểu cho DWG export khi không tìm thấy INI chuẩn của Inventor.
        /// Lưu tại %APPDATA%\MCGCadPlugin\DWG-AutoCAD Export.ini.
        /// </summary>
        private string CreateMinimalDwgIni()
        {
            try
            {
                string iniPath = Path.Combine(FileLogger.LogDirectory, "DWG-AutoCAD Export.ini");
                if (File.Exists(iniPath)) return iniPath;

                // Nội dung INI tối thiểu — dùng default DWG 2018 format
                string content =
                    "[System]\r\n" +
                    "Version=2\r\n" +
                    "Language=ENU\r\n" +
                    "\r\n" +
                    "[Export]\r\n" +
                    "Export_Acad_Version=27\r\n" +
                    "Export_Acad_Revision=0\r\n" +
                    "\r\n";

                File.WriteAllText(iniPath, content);
                return iniPath;
            }
            catch (System.Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "tạo INI tối thiểu", ex);
                return null;
            }
        }

        /// <summary>
        /// Tìm đường dẫn file "DWG-AutoCAD Export.ini" trong các phiên bản Inventor đã cài.
        /// Ưu tiên phiên bản mới nhất trước.
        /// </summary>
        private string FindInventorDwgIniPath()
        {
            string publicDocs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);

            // Duyệt từ phiên bản mới nhất xuống cũ nhất
            string[] versions = { "2026", "2025", "2024", "2023", "2022", "2021", "2020" };
            string[] iniNames = { "DWG-AutoCAD Export.ini", "DWG AutoCAD Export.ini" };

            foreach (string version in versions)
            {
                foreach (string iniName in iniNames)
                {
                    string path = Path.Combine(publicDocs,
                        "Autodesk", $"Inventor {version}", "Design Data", iniName);
                    if (File.Exists(path))
                        return path;
                }
            }

            // Thử luôn thư mục C:\ProgramData (một số bản Inventor để INI ở đây)
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            foreach (string version in versions)
            {
                foreach (string iniName in iniNames)
                {
                    string path = Path.Combine(programData,
                        "Autodesk", $"Inventor {version}", "Design Data", iniName);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
        }

        #endregion
    }
}
