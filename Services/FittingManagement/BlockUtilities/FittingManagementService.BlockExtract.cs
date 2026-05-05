using System;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace MCGCadPlugin.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        public void ExtractEntitiesFromBlock()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu ExtractEntitiesFromBlock...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int extractCount = 0;

            while (true)
            {
                PromptNestedEntityOptions pneo = new PromptNestedEntityOptions("\nSelect nested object to extract [Press Enter/Esc to Finish]: ");
                pneo.AllowNone = true;
                
                PromptNestedEntityResult pner = ed.GetNestedEntity(pneo);

                if (pner.Status == PromptStatus.Cancel || pner.Status == PromptStatus.None) break;
                if (pner.Status != PromptStatus.OK) continue;

                if (pner.GetContainers().Length == 0)
                {
                    ed.WriteMessage("\nThe selected object is not inside a Block.");
                    continue;
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            Entity nestedEnt = tr.GetObject(pner.ObjectId, OpenMode.ForWrite) as Entity;
                            if (nestedEnt == null) continue;

                            Entity extractedEnt = nestedEnt.Clone() as Entity;
                            extractedEnt.TransformBy(pner.Transform);

                            BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) as BlockTableRecord;
                            currentSpace.AppendEntity(extractedEnt);
                            tr.AddNewlyCreatedDBObject(extractedEnt, true);

                            nestedEnt.Erase();

                            ObjectId parentBlockId = pner.GetContainers()[0];
                            BlockReference parentBlock = tr.GetObject(parentBlockId, OpenMode.ForWrite) as BlockReference;
                            if (parentBlock != null)
                            {
                                parentBlock.RecordGraphicsModified(true);
                            }

                            tr.Commit();
                            extractCount++;
                            ed.WriteMessage($"\n>> Extracted {extractCount} object(s).");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"{LOG_PREFIX} LỖI Extract Entities: {ex.Message}");
                            throw;
                        }
                    }
                }
            }

            if (extractCount > 0)
            {
                ed.WriteMessage($"\n[Complete] Total {extractCount} object(s) extracted.");
                ed.Regen();
                Debug.WriteLine($"{LOG_PREFIX} Extract THÀNH CÔNG.");
            }
        }
    }
}