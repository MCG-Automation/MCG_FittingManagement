using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities;

namespace MCGCadPlugin.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        public async Task<ImportResult> CollectIdwDrawingsAsync(string[] idwPaths, IProgress<string> progress = null)
        {
            FileLogger.LogSessionStart($"CollectIdwDrawings ({idwPaths?.Length ?? 0} files)");
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu CollectIdwDrawingsAsync — {idwPaths?.Length ?? 0} files.");

            if (idwPaths == null || idwPaths.Length == 0)
                return new ImportResult();

            var result = new ImportResult();
            List<string> tempDwgPaths = new List<string>();
            string tempDir = Path.Combine(Path.GetTempPath(), "MCG_DrawingCollection_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempDir);
                FileLogger.Log(LOG_PREFIX, $"Đã tạo thư mục tạm cho IDW Collection: {tempDir}");

                progress?.Report("Starting Inventor in background...");

                // Chạy quá trình export IDW sang DWG trên worker thread
                await Task.Run(() =>
                {
                    bool weStartedInventor = false;
                    dynamic invApp = null;
                    try
                    {
                        invApp = AcquireInventorInstance(out weStartedInventor);

                        // Mỗi IDW export vào subfolder index riêng (001/, 002/, …) → không collision
                        // kể cả khi user chọn nhiều file khác folder cùng tên. Filename của DWG tạm giữ
                        // nguyên baseName → CollectDrawingsAsync dùng baseName làm prefix block,
                        // block cuối cùng trong nhà kho giữ đúng intent (vd 'Detail' chứ không phải '001_Detail').
                        for (int i = 0; i < idwPaths.Length; i++)
                        {
                            string idwPath = idwPaths[i];
                            string baseName = Path.GetFileNameWithoutExtension(idwPath);

                            string perFileDir = Path.Combine(tempDir, (i + 1).ToString("D3"));
                            Directory.CreateDirectory(perFileDir);
                            string tempDwgPath = Path.Combine(perFileDir, baseName + ".dwg");

                            progress?.Report($"[{i + 1}/{idwPaths.Length}] Converting IDW: {baseName}");
                            FileLogger.Log(LOG_PREFIX, $"[IDW Collection] Xử lý: {idwPath} → {tempDwgPath}");

                            dynamic drawingDoc = null;
                            try
                            {
                                drawingDoc = invApp.Documents.Open(idwPath, false); // OpenVisible=false
                                ExportIdwToDwg(invApp, drawingDoc, tempDwgPath);

                                if (File.Exists(tempDwgPath))
                                {
                                    tempDwgPaths.Add(tempDwgPath);
                                    FileLogger.Log(LOG_PREFIX, $"  Export thành công: {tempDwgPath}");
                                }
                                else
                                {
                                    result.AddError(baseName, "Failed to create DWG file.");
                                    result.FailCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogException(LOG_PREFIX, $"export {baseName}", ex);
                                result.AddError(baseName, $"Export error: {ex.Message}");
                                result.FailCount++;
                            }
                            finally
                            {
                                if (drawingDoc != null)
                                {
                                    try
                                    {
                                        drawingDoc.Close(true); // true = skip save
                                        Marshal.ReleaseComObject(drawingDoc);
                                    }
                                    catch (Exception exClose)
                                    {
                                        FileLogger.Log(LOG_PREFIX, $"  Lỗi đóng document {baseName}: {exClose.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogException(LOG_PREFIX, "Khởi tạo Inventor cho IDW Collection", ex);
                        throw;
                    }
                    finally
                    {
                        ReleaseInventorInstance(invApp, weStartedInventor);
                    }
                });

                if (tempDwgPaths.Count > 0)
                {
                    progress?.Report($"Collecting {tempDwgPaths.Count} drawing(s)...");
                    FileLogger.Log(LOG_PREFIX, $"Chuyển qua luồng CollectDrawingsAsync với {tempDwgPaths.Count} file DWG tạm...");

                    // Gọi luồng hiện tại để gom DWG. CollectDrawingsAsync dùng tên file làm prefix block;
                    // temp DWG giữ nguyên baseName (subfolder 001/ 002/ … đã xử lý collision) nên
                    // block name trong nhà kho = baseName của IDW gốc.
                    ImportResult dwgResult = await CollectDrawingsAsync(tempDwgPaths.ToArray(), progress);

                    // Gộp kết quả
                    result.SuccessCount += dwgResult.SuccessCount;
                    result.FailCount += dwgResult.FailCount;
                    foreach (var err in dwgResult.Errors)
                    {
                        result.Errors.Add(err);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogException(LOG_PREFIX, "CollectIdwDrawingsAsync tổng thể", ex);
                throw;
            }
            finally
            {
                // Dọn dẹp thư mục tạm
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                        FileLogger.Log(LOG_PREFIX, $"Đã dọn dẹp thư mục tạm: {tempDir}");
                    }
                }
                catch (Exception exCleanup)
                {
                    FileLogger.LogException(LOG_PREFIX, "dọn dẹp thư mục tạm", exCleanup);
                }
            }

            return result;
        }
    }
}
