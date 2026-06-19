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
        public void AddEntitiesToBlock()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu AddEntitiesToBlock...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions peoBlock = new PromptEntityOptions("\nSelect the target Block: ");
            peoBlock.SetRejectMessage("\nObject must be a Block Reference.");
            peoBlock.AddAllowedClass(typeof(BlockReference), true);
            PromptEntityResult perBlock = ed.GetEntity(peoBlock);

            if (perBlock.Status != PromptStatus.OK) return;

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect objects to add into the Block: ";
            PromptSelectionResult psr = ed.GetSelection(pso);

            if (psr.Status != PromptStatus.OK || psr.Value.Count == 0) return;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockReference blkRef = tr.GetObject(perBlock.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (blkRef == null) return;

                        BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForWrite) as BlockTableRecord;

                        Matrix3d blockTransform = blkRef.BlockTransform;
                        Matrix3d inverseTransform = blockTransform.Inverse();

                        foreach (SelectedObject selObj in psr.Value)
                        {
                            Entity sourceEnt = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite) as Entity;
                            if (sourceEnt == null) continue;

                            Entity clonedEnt = sourceEnt.Clone() as Entity;
                            clonedEnt.TransformBy(inverseTransform);

                            btr.AppendEntity(clonedEnt);
                            tr.AddNewlyCreatedDBObject(clonedEnt, true);

                            sourceEnt.Erase();
                        }

                        blkRef.UpgradeOpen();
                        blkRef.RecordGraphicsModified(true);

                        tr.Commit();
                        ed.WriteMessage($"\nSuccessfully added {psr.Value.Count} object(s) to Block '{btr.Name}'.");
                        Debug.WriteLine($"{LOG_PREFIX} Add Entities THÀNH CÔNG.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} LỖI AddEntitiesToBlock: {ex.Message}");
                        throw;
                    }
                }
            }
        }
    }
}