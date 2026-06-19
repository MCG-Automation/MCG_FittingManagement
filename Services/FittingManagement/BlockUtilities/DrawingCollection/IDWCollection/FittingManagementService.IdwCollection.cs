using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Services.FittingManagement
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

                progress?.Report("Đang khởi động Inventor dưới nền...");
                
                // Chạy quá trình export IDW sang DWG trên worker thread
                await Task.Run(() =>
                {
                    bool weStartedInventor = false;
                    dynamic invApp = null;
                    try
                    {
                        invApp = AcquireInventorInstance(out weStartedInventor);

                        for (int i = 0; i < idwPaths.Length; i++)
                        {
                            string idwPath = idwPaths[i];
                            string baseName = Path.GetFileNameWithoutExtension(idwPath);
                            string tempDwgPath = Path.Combine(tempDir, baseName + ".dwg");
                            
                            progress?.Report($"Đang xử lý IDW sang DWG: {baseName} ({i + 1}/{idwPaths.Length})");
                            FileLogger.Log(LOG_PREFIX, $"[IDW Collection] Xử lý: {idwPath}");

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
                                    result.AddError(baseName, "Lỗi: Không tạo được file DWG.");
                                    result.FailCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                FileLogger.LogException(LOG_PREFIX, $"export {baseName}", ex);
                                result.AddError(baseName, $"Lỗi export: {ex.Message}");
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
                    progress?.Report($"Đang gom {tempDwgPaths.Count} bản vẽ DWG...");
                    FileLogger.Log(LOG_PREFIX, $"Chuyển qua luồng CollectDrawingsAsync với {tempDwgPaths.Count} file DWG tạm...");
                    
                    // Gọi luồng hiện tại để gom DWG.
                    // CollectDrawingsAsync sẽ lo việc tính toán, wblock và đổi tên block (tên block dùng tên file gốc do file tạm vẫn giữ tên gốc)
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
