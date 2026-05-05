using System;
using System.IO;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// IDW Import — phần export Inventor drawing sang DWG: thử <c>Document.SaveAs</c> trước (đơn giản),
    /// fallback DWG Translator Add-In với INI khi cần. Bao gồm helper tìm/tạo INI.
    /// </summary>
    public partial class FittingManagementService
    {
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
    }
}
