using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCGCadPlugin.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        // ====================================================================
        // 2. INTERACTIVE BALLOON (Click cắm bóng thủ công)
        // ====================================================================
        public void InteractivePlaceBalloon()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu InteractivePlaceBalloon...");
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor ed = doc.Editor;

                // Method này gọi từ Palette button (WPF context) — KHÔNG có auto-lock như [CommandMethod].
                // Phải LockDocument trước khi mở transaction ghi, nếu không sẽ throw eLockViolation
                // tại dòng OpenMode.ForWrite ở BlockTableRecord btrSpace.
                using (DocumentLock docLock = doc.LockDocument())
                {
                while (true)
                {
                    PromptNestedEntityOptions pneo = new PromptNestedEntityOptions("\nSelect Fitting to balloon (or press ESC to exit): ");
                    PromptNestedEntityResult pner = ed.GetNestedEntity(pneo);

                    if (pner.Status != PromptStatus.OK) break;

                    Point3d arrowHeadPoint = pner.PickedPoint;
                    ObjectId[] containers = pner.GetContainers();

                    if (containers == null || containers.Length == 0)
                    {
                        ed.WriteMessage("\nSelected entity is not inside a Block! Try again.");
                        continue;
                    }

                    string rawPosNumber = "";
                    bool foundPos = false;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId containerId in containers)
                        {
                            BlockReference blkRef = tr.GetObject(containerId, OpenMode.ForRead) as BlockReference;
                            if (blkRef == null || blkRef.AttributeCollection == null) continue;

                            foreach (ObjectId attId in blkRef.AttributeCollection)
                            {
                                AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                if (attRef != null && attRef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                                {
                                    rawPosNumber = attRef.TextString;
                                    foundPos = true;
                                    break; 
                                }
                            }
                            if (foundPos) break; 
                        }

                        if (!foundPos || string.IsNullOrWhiteSpace(rawPosNumber))
                        {
                            ed.WriteMessage("\nFitting does not have a POS_NUM assigned or it is empty. Sync BOM first!");
                            continue;
                        }

                        PromptPointOptions ppo = new PromptPointOptions($"\nPlace balloon for Pos [{rawPosNumber}] (or press ESC to exit): ");
                        ppo.UseBasePoint = true;
                        ppo.BasePoint = arrowHeadPoint; 

                        PromptPointResult ppr = ed.GetPoint(ppo);
                        if (ppr.Status != PromptStatus.OK) break;

                        Point3d balloonPoint = ppr.Value;

                        // Lấy scale từ A1 BlockReference chứa arrowHeadPoint (ScaleFactors.X).
                        // Không tìm thấy A1 → fallback 25.0 (giữ behavior cũ).
                        double mleaderScale = ComputeA1Scale(tr, db, arrowHeadPoint) ?? 25.0;

                        BlockTableRecord btrSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        DrawMagneticMLeader(tr, btrSpace, db, arrowHeadPoint, balloonPoint, rawPosNumber, mleaderScale);

                        tr.Commit();
                    }
                }
                }
                Debug.WriteLine($"{LOG_PREFIX} Thoát InteractivePlaceBalloon an toàn.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI InteractivePlaceBalloon: {ex.Message}");
                throw;
            }
        }
    }
}