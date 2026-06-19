using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        public void ChangeBlockBasePoint()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu ChangeBlockBasePoint...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect block to change base point: ");
                peo.SetRejectMessage("\nPlease select a valid Block Reference.");
                peo.AddAllowedClass(typeof(BlockReference), true);
                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status != PromptStatus.OK) return;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockReference blkRef = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null) return;

                        ObjectId btrId = blkRef.IsDynamicBlock ? blkRef.DynamicBlockTableRecord : blkRef.BlockTableRecord;

                        PromptPointOptions ppo = new PromptPointOptions("\nPick the NEW base point: ");
                        ppo.UseBasePoint = true;
                        ppo.BasePoint = blkRef.Position;
                        PromptPointResult ppr = ed.GetPoint(ppo);

                        if (ppr.Status != PromptStatus.OK) return;
                        Point3d pNewWcs = ppr.Value;

                        Matrix3d matOcs2Wcs = blkRef.BlockTransform;
                        Matrix3d matWcs2Ocs = matOcs2Wcs.Inverse();

                        Point3d pNewOcs = pNewWcs.TransformBy(matWcs2Ocs);
                        Vector3d vecMoveEntities = Point3d.Origin - pNewOcs;

                        BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForWrite) as BlockTableRecord;

                        foreach (ObjectId id in btr)
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                            if (ent != null) ent.TransformBy(Matrix3d.Displacement(vecMoveEntities));
                        }

                        ObjectIdCollection refIds = btr.GetBlockReferenceIds(true, true);
                        foreach (ObjectId id in refIds)
                        {
                            BlockReference br = tr.GetObject(id, OpenMode.ForWrite) as BlockReference;
                            if (br != null)
                            {
                                Point3d correctedPos = pNewOcs.TransformBy(br.BlockTransform);
                                br.Position = correctedPos;
                            }
                        }

                        tr.Commit();
                        ed.WriteMessage($"\nSuccessfully changed base point for Block '{btr.Name}'.");
                        Debug.WriteLine($"{LOG_PREFIX} Đổi Base Point THÀNH CÔNG.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} LỖI ChangeBlockBasePoint: {ex.Message}");
                        throw;
                    }
                }
                ed.Regen();
            }
        }
    }
}