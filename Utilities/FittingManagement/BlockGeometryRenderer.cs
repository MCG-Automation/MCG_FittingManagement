using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace MCG_FittingManagement.Utilities.FittingManagement
{
    /// <summary>
    /// Render hình học của 1 BlockTableRecord (hoặc ModelSpace) ra <see cref="Bitmap"/> 2D wireframe.
    /// Mục đích: tạo preview cho file .dwg KHÔNG có embedded thumbnail / PreviewIcon.
    ///
    /// Coverage:
    ///   - Line, Polyline (LWPolyline), Circle, Arc, Ellipse, Solid → vẽ trực tiếp
    ///   - BlockReference → recurse, áp dụng BlockTransform
    ///   - Text/MText/Hatch/Attribute → bỏ qua (không quan trọng cho preview)
    ///
    /// Ý tưởng: chiếu XY (bỏ Z), uniform scale theo bbox, flip Y (CAD up → Bitmap down), margin 12px.
    /// </summary>
    public static class BlockGeometryRenderer
    {
        private const string LOG_PREFIX = "[BlockGeometryRenderer]";
        private const int MARGIN_PX = 12;
        private const int BULGE_SEGMENTS = 8;

        /// <summary>
        /// Render block geometry từ database đã mở. Trả về null nếu BTR rỗng / không có hình học.
        /// Caller chịu trách nhiệm dispose Bitmap.
        /// </summary>
        public static Bitmap RenderFromDb(Database db, string preferredBlockName, int width = 280, int height = 280)
        {
            if (db == null) return null;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                    // 1. Tìm BTR target: ưu tiên block name; fallback ModelSpace (Wblock'd file)
                    ObjectId btrId = ObjectId.Null;
                    if (!string.IsNullOrEmpty(preferredBlockName) && bt.Has(preferredBlockName))
                        btrId = bt[preferredBlockName];
                    if (btrId.IsNull && bt.Has(BlockTableRecord.ModelSpace))
                        btrId = bt[BlockTableRecord.ModelSpace];
                    if (btrId.IsNull) return null;

                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                    // 2. Compute extents (chỉ top-level entities — Bounds của BlockReference đã transformed)
                    var bbox = ComputeExtents(tr, btr);
                    if (bbox == null) return null;

                    var min = bbox.Value.MinPoint;
                    var max = bbox.Value.MaxPoint;
                    double contentW = max.X - min.X;
                    double contentH = max.Y - min.Y;
                    if (contentW <= double.Epsilon && contentH <= double.Epsilon) return null;

                    // 3. Setup transform CAD -> pixel
                    var ctx = new RenderContext(min, max, width, height, MARGIN_PX);

                    // 4. Draw
                    var bmp = new Bitmap(width, height);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.White);

                        DrawBtrRecursive(g, tr, btr, Matrix3d.Identity, ctx);
                    }

                    tr.Commit();
                    Debug.WriteLine($"{LOG_PREFIX} Rendered block '{preferredBlockName ?? BlockTableRecord.ModelSpace}' ({width}x{height}).");
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI Render: {ex.Message}");
                return null;
            }
        }

        // ============ Extents ============

        private static Extents3d? ComputeExtents(Transaction tr, BlockTableRecord btr)
        {
            Extents3d? acc = null;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (IsSkippable(ent)) continue;

                try
                {
                    var b = ent.GeometricExtents;
                    if (acc == null)
                    {
                        acc = new Extents3d(b.MinPoint, b.MaxPoint);
                    }
                    else
                    {
                        var cur = acc.Value;
                        cur.AddExtents(b);
                        acc = cur;
                    }
                }
                catch
                {
                    // entity không hỗ trợ extents (vd: Hatch rỗng) — bỏ qua
                }
            }
            return acc;
        }

        // ============ Recursive draw ============

        private static void DrawBtrRecursive(Graphics g, Transaction tr, BlockTableRecord btr, Matrix3d xform, RenderContext ctx)
        {
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is BlockReference blkRef)
                {
                    BlockTableRecord nested = null;
                    try
                    {
                        nested = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    }
                    catch { continue; }

                    if (nested != null)
                    {
                        var combined = xform * blkRef.BlockTransform;
                        DrawBtrRecursive(g, tr, nested, combined, ctx);
                    }
                    continue;
                }

                if (IsSkippable(ent)) continue;

                using (var pen = MakePen(ent))
                {
                    DrawEntity(g, pen, ent, xform, ctx);
                }
            }
        }

        private static bool IsSkippable(Entity ent)
        {
            // Bỏ qua entity không quan trọng cho preview hình học
            return ent is DBText || ent is MText
                || ent is Hatch || ent is AttributeDefinition
                || ent is AttributeReference;
        }

        // ============ Draw từng entity ============

        private static void DrawEntity(Graphics g, Pen pen, Entity ent, Matrix3d xform, RenderContext ctx)
        {
            try
            {
                switch (ent)
                {
                    case Line line:
                        g.DrawLine(pen, ctx.ToPx(line.StartPoint.TransformBy(xform)), ctx.ToPx(line.EndPoint.TransformBy(xform)));
                        break;
                    case Polyline pl:
                        DrawLwPolyline(g, pen, pl, xform, ctx);
                        break;
                    // Polyline2d (DXF heavy-polyline) hiếm gặp trong fitting → bỏ qua v1
                    case Circle circle:
                        DrawCircle(g, pen, circle, xform, ctx);
                        break;
                    case Arc arc:
                        DrawArc(g, pen, arc, xform, ctx);
                        break;
                    case Ellipse el:
                        DrawEllipse(g, pen, el, xform, ctx);
                        break;
                    case Solid solid:
                        DrawSolid(g, pen, solid, xform, ctx);
                        break;
                    // các entity còn lại bỏ qua trong v1
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} Lỗi vẽ {ent.GetType().Name}: {ex.Message}");
            }
        }

        private static void DrawLwPolyline(Graphics g, Pen pen, Polyline pl, Matrix3d xform, RenderContext ctx)
        {
            int n = pl.NumberOfVertices;
            if (n < 2) return;

            var pts = new List<PointF>(n + 8);
            for (int i = 0; i < n; i++)
            {
                var v = pl.GetPoint2dAt(i);
                pts.Add(ctx.ToPx(new Point3d(v.X, v.Y, pl.Elevation).TransformBy(xform)));

                double bulge = pl.GetBulgeAt(i);
                if (Math.Abs(bulge) > 1e-9 && (i < n - 1 || pl.Closed))
                {
                    int next = (i + 1) % n;
                    var nv = pl.GetPoint2dAt(next);
                    AppendBulgeSegments(pts, v, nv, bulge, pl.Elevation, xform, ctx);
                }
            }
            if (pl.Closed && pts.Count > 0) pts.Add(pts[0]);

            if (pts.Count >= 2) g.DrawLines(pen, pts.ToArray());
        }

        private static void AppendBulgeSegments(List<PointF> pts, Point2d p1, Point2d p2, double bulge, double z, Matrix3d xform, RenderContext ctx)
        {
            // bulge = tan(theta/4); theta = sweep angle of arc
            double theta = 4.0 * Math.Atan(bulge);
            double chord = (p2 - p1).Length;
            if (chord < 1e-9) return;
            double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));

            // Center của arc: vuông góc với chord midpoint, dịch theo chiều bulge
            var mid = new Point2d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0);
            var dir = (p2 - p1).GetNormal();
            var perp = new Vector2d(-dir.Y, dir.X);
            double dist = radius * Math.Cos(theta / 2.0);
            // sign: bulge>0 = arc bên trái khi đi từ p1→p2
            var center = mid + perp * (Math.Sign(bulge) * dist);

            double a1 = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
            double sweep = theta;

            for (int s = 1; s <= BULGE_SEGMENTS; s++)
            {
                double t = (double)s / BULGE_SEGMENTS;
                double a = a1 + sweep * t;
                double x = center.X + radius * Math.Cos(a);
                double y = center.Y + radius * Math.Sin(a);
                pts.Add(ctx.ToPx(new Point3d(x, y, z).TransformBy(xform)));
            }
        }

        private static void DrawCircle(Graphics g, Pen pen, Circle c, Matrix3d xform, RenderContext ctx)
        {
            var center = c.Center.TransformBy(xform);
            double r = c.Radius * AverageScale(xform);
            var pc = ctx.ToPx(center);
            float pr = (float)(r * ctx.Scale);
            if (pr < 0.5f) return;
            g.DrawEllipse(pen, pc.X - pr, pc.Y - pr, pr * 2, pr * 2);
        }

        private static void DrawArc(Graphics g, Pen pen, Arc arc, Matrix3d xform, RenderContext ctx)
        {
            var center = arc.Center.TransformBy(xform);
            double r = arc.Radius * AverageScale(xform);
            var pc = ctx.ToPx(center);
            float pr = (float)(r * ctx.Scale);
            if (pr < 0.5f) return;

            // CAD: angle CCW, GDI+ DrawArc: angle CW từ trục X dương; flip Y → đổi dấu góc
            // Cách an toàn: tessellate thành line segments giống bulge
            int segs = 24;
            var pts = new PointF[segs + 1];
            double a1 = arc.StartAngle;
            double sweep = arc.EndAngle - arc.StartAngle;
            if (sweep < 0) sweep += 2 * Math.PI;
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                double a = a1 + sweep * t;
                double x = arc.Center.X + arc.Radius * Math.Cos(a);
                double y = arc.Center.Y + arc.Radius * Math.Sin(a);
                pts[i] = ctx.ToPx(new Point3d(x, y, arc.Center.Z).TransformBy(xform));
            }
            g.DrawLines(pen, pts);
        }

        private static void DrawEllipse(Graphics g, Pen pen, Ellipse el, Matrix3d xform, RenderContext ctx)
        {
            // Tessellate ellipse thành line segments
            int segs = 32;
            var pts = new PointF[segs + 1];
            double startParam = el.StartParam;
            double endParam = el.EndParam;
            double sweep = endParam - startParam;
            if (Math.Abs(sweep) < 1e-9) sweep = 2 * Math.PI;

            for (int i = 0; i <= segs; i++)
            {
                double t = startParam + sweep * ((double)i / segs);
                var pt = el.GetPointAtParameter(t).TransformBy(xform);
                pts[i] = ctx.ToPx(pt);
            }
            g.DrawLines(pen, pts);
        }

        private static void DrawSolid(Graphics g, Pen pen, Solid solid, Matrix3d xform, RenderContext ctx)
        {
            try
            {
                var p0 = solid.GetPointAt(0).TransformBy(xform);
                var p1 = solid.GetPointAt(1).TransformBy(xform);
                var p2 = solid.GetPointAt(2).TransformBy(xform);
                var p3 = solid.GetPointAt(3).TransformBy(xform);
                // Solid trong AutoCAD: vertex order p0, p1, p3, p2 → quad
                var pts = new[] { ctx.ToPx(p0), ctx.ToPx(p1), ctx.ToPx(p3), ctx.ToPx(p2) };
                using (var brush = new SolidBrush(Color.FromArgb(80, pen.Color)))
                {
                    g.FillPolygon(brush, pts);
                }
                g.DrawPolygon(pen, pts);
            }
            catch { }
        }

        // ============ Helpers ============

        private static double AverageScale(Matrix3d m)
        {
            // |det|^(1/3) cho 3D, nhưng preview 2D — dùng trung bình của X/Y row magnitudes
            var r0 = new Vector3d(m[0, 0], m[1, 0], m[2, 0]).Length;
            var r1 = new Vector3d(m[0, 1], m[1, 1], m[2, 1]).Length;
            return (r0 + r1) / 2.0;
        }

        private static Pen MakePen(Entity ent)
        {
            // V1: pen 1px đen. Color refinement (ColorIndex → RGB) có thể thêm sau.
            return new Pen(Color.FromArgb(40, 40, 40), 1f);
        }

        // ============ Render context ============

        private class RenderContext
        {
            public double CenterX, CenterY;
            public double Scale;
            public float HalfW, HalfH;

            public RenderContext(Point3d min, Point3d max, int width, int height, int margin)
            {
                CenterX = (min.X + max.X) / 2.0;
                CenterY = (min.Y + max.Y) / 2.0;
                double availW = Math.Max(1, width - 2 * margin);
                double availH = Math.Max(1, height - 2 * margin);
                double contentW = Math.Max(1e-9, max.X - min.X);
                double contentH = Math.Max(1e-9, max.Y - min.Y);
                Scale = Math.Min(availW / contentW, availH / contentH);
                HalfW = width / 2f;
                HalfH = height / 2f;
            }

            public PointF ToPx(Point3d p)
            {
                float x = (float)((p.X - CenterX) * Scale + HalfW);
                float y = (float)((CenterY - p.Y) * Scale + HalfH); // Y flip
                return x.IsFiniteSafe() && y.IsFiniteSafe() ? new PointF(x, y) : new PointF(HalfW, HalfH);
            }
        }
    }

    internal static class FloatExt
    {
        public static bool IsFiniteSafe(this float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
}
