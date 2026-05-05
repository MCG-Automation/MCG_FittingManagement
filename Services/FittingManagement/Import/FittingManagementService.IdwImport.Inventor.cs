using System;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// IDW Import — phần Inventor COM lifecycle: acquire/release Inventor instance + xử lý 1 file IDW
    /// (mở doc, lấy referenced model, gọi extract metadata/views, export DWG, lưu JSON, đóng doc).
    /// </summary>
    public partial class FittingManagementService
    {
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
    }
}
