using System;
using System.Collections.Generic;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// IDW Import — phần trích xuất Drawing Views: convert tâm/kích thước từ sheet coords (cm)
    /// sang model coords (mm), phân loại 2D ortho vs 3D iso/arbitrary qua 2 heuristic
    /// (Camera.ViewOrientationType + Camera direction vector).
    /// </summary>
    public partial class FittingManagementService
    {
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

                            // Phân loại 2D ortho vs 3D iso/arbitrary — dùng 2 heuristic độc lập (OR):
                            //   (1) Camera.ViewOrientationType: Iso (10309-10312) + Arbitrary (10301) = 3D.
                            //   (2) Camera direction vector: ortho 2D = dọc 1 trục chính (|max| ≈ 1, các thành phần khác ≈ 0).
                            //       Iso 3D ví dụ (1,1,1)/√3 → mỗi thành phần ≈ 0.577 → max ≈ 0.577 < 0.95 → 3D.
                            // Heuristic (2) bắt được iso views mà Inventor báo orient = kCurrent (10303) hoặc kDefault (10313)
                            // — case enum không kê khai chính xác orient.
                            bool is3D = false;
                            bool isPlanView = false;
                            int orientCode = -1;
                            double nx = double.NaN, ny = double.NaN, nz = double.NaN;
                            try
                            {
                                try { orientCode = (int)drawingView.Camera.ViewOrientationType; } catch { }
                                bool isoByEnum = (orientCode == 10301) || (orientCode >= 10309 && orientCode <= 10312);

                                bool isoByVector = false;
                                try
                                {
                                    dynamic eye = drawingView.Camera.Eye;
                                    dynamic target = drawingView.Camera.Target;
                                    double dx = (double)target.X - (double)eye.X;
                                    double dy = (double)target.Y - (double)eye.Y;
                                    double dz = (double)target.Z - (double)eye.Z;
                                    double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                                    if (len > 1e-6)
                                    {
                                        nx = Math.Abs(dx / len);
                                        ny = Math.Abs(dy / len);
                                        nz = Math.Abs(dz / len);
                                        double maxComp = Math.Max(nx, Math.Max(ny, nz));
                                        // 2D ortho: max gần 1 (dọc trục chính). Iso/skew: max < 0.95.
                                        isoByVector = maxComp < 0.95;

                                        // Plan/Top view: trục Z chiếm ưu thế (nhìn thẳng từ trên xuống) +
                                        // camera hướng XUỐNG (dz < 0 — quy ước Z model hướng lên, chuẩn kỹ
                                        // thuật). Chỉ set true khi đây thật sự là 2D ortho (maxComp ≥ 0.95),
                                        // tránh nhận nhầm view iso gần trục Z là Plan.
                                        isPlanView = maxComp >= 0.95 && nz >= nx && nz >= ny && dz < 0;
                                    }
                                }
                                catch { /* không đọc được Eye/Target — bỏ qua heuristic vector */ }

                                is3D = isoByEnum || isoByVector;

                                FileLogger.Log(LOG_PREFIX,
                                    $"    View_{viewIndex}: orient={orientCode}, dir=({nx:F2},{ny:F2},{nz:F2}), isoByEnum={isoByEnum}, isoByVector={isoByVector} → {(is3D ? "3D" : "2D")}, isPlanView={isPlanView}");
                            }
                            catch (System.Exception exOrient)
                            {
                                FileLogger.Log(LOG_PREFIX, $"  CẢNH BÁO: phân loại 2D/3D fail cho View_{viewIndex} — treat as 2D: {exOrient.Message}");
                            }

                            views.Add(new ViewMetadata
                            {
                                Name = "View_" + viewIndex,
                                CenterX = cxSheet * baseScaleFactor,
                                CenterY = cySheet * baseScaleFactor,
                                Width = wSheet * baseScaleFactor,
                                Height = hSheet * baseScaleFactor,
                                Is3D = is3D,
                                IsPlanView = isPlanView
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

            int n3D = 0; foreach (var v in views) if (v.Is3D) n3D++;
            int n2D = views.Count - n3D;
            FileLogger.Log(LOG_PREFIX, $"  Tìm thấy {views.Count} drawing view(s) (tọa độ model mm) — {n2D} 2D ortho, {n3D} 3D iso/arbitrary.");
            return views;
        }
    }
}
