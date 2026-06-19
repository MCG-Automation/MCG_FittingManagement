using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        // ====================================================================
        // 1. MASS AUTO-BALLOON (Quét vùng chọn & Cắm bóng hàng loạt)
        // ====================================================================
        public void MassAutoBalloon()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu MassAutoBalloon...");
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor ed = doc.Editor;

                using (DocumentLock docLock = doc.LockDocument())
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\nSelect Panel or Details blocks to mass-balloon: ";
                    TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                    PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));

                    if (psr.Status != PromptStatus.OK) return;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        const double FALLBACK_SCALE = 25.0;
                        List<ObjectId> selectedIds = new List<ObjectId>(psr.Value.GetObjectIds());

                        List<DiscoveredFitting> foundFittings = new List<DiscoveredFitting>();
                        HashSet<string> balloonedPos = new HashSet<string>();

                        foreach (ObjectId id in selectedIds)
                        {
                            BlockReference topBlk = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                            if (topBlk != null)
                            {
                                DiscoverFittings(tr, topBlk, topBlk.BlockTransform, balloonedPos, foundFittings);
                            }
                        }

                        if (foundFittings.Count == 0)
                        {
                            ed.WriteMessage("\nNo valid fittings with POS_NUM found in selection.");
                            return;
                        }

                        // Per-fitting scale theo A1 chứa fitting đó. Slot layout dùng max scale
                        // (đảm bảo slot đủ rộng cho balloon size lớn nhất trong cluster).
                        var perFittingScale = new Dictionary<DiscoveredFitting, double>();
                        double maxScale = 0;
                        double minX = double.MaxValue;
                        double maxX = double.MinValue;
                        foreach (var f in foundFittings)
                        {
                            double s = ComputeA1Scale(tr, db, f.ArrowPoint) ?? FALLBACK_SCALE;
                            perFittingScale[f] = s;
                            if (s > maxScale) maxScale = s;
                            if (f.ArrowPoint.X < minX) minX = f.ArrowPoint.X;
                            if (f.ArrowPoint.X > maxX) maxX = f.ArrowPoint.X;
                        }

                        double margin = 20.0 * maxScale;
                        double slotSpacing = 12.0 * maxScale;
                        double leftBoundary = minX - margin;
                        double rightBoundary = maxX + margin;

                        List<Point3d> occupiedSlots = new List<Point3d>();
                        BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        foreach (var f in foundFittings)
                        {
                            double distLeft = Math.Abs(f.ArrowPoint.X - leftBoundary);
                            double distRight = Math.Abs(rightBoundary - f.ArrowPoint.X);

                            double targetX = (distLeft < distRight) ? leftBoundary : rightBoundary;
                            double targetY = f.ArrowPoint.Y;

                            Point3d candidate = new Point3d(targetX, targetY, 0);

                            int attempt = 1;
                            while (IsSlotOccupied(candidate, occupiedSlots, slotSpacing))
                            {
                                double offset = slotSpacing * ((attempt + 1) / 2);
                                if (attempt % 2 != 0) offset = -offset;
                                candidate = new Point3d(targetX, targetY + offset, 0);
                                attempt++;
                            }

                            DrawMagneticMLeader(tr, currentSpace, db, f.ArrowPoint, candidate, f.PosNum, perFittingScale[f]);
                            occupiedSlots.Add(candidate);
                        }

                        tr.Commit();
                        ed.WriteMessage($"\nMass Ballooning complete! Placed {foundFittings.Count} smart balloon clusters.");
                        Debug.WriteLine($"{LOG_PREFIX} MassAutoBalloon THÀNH CÔNG.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI MassAutoBalloon: {ex.Message}");
                throw;
            }
        }
    }
}