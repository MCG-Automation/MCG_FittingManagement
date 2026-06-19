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
        public void InteractiveBlockRenameClone()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu InteractiveBlockRenameClone...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect a Block to Rename or Clone: ");
            peo.SetRejectMessage("\nPlease select a Block Reference only.");
            peo.AddAllowedClass(typeof(BlockReference), true);
            
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            string originalName = "";
            ObjectId blockRefId = per.ObjectId;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockReference blkRef = tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
                    if (blkRef == null) return;

                    originalName = blkRef.IsDynamicBlock ? 
                        ((BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name : 
                        blkRef.Name;

                    PromptStringOptions pso = new PromptStringOptions($"\nEnter new block name <{originalName}_New>: ");
                    pso.AllowSpaces = true;
                    pso.DefaultValue = $"{originalName}_New"; 
                    pso.UseDefaultValue = true;               

                    PromptResult prName = ed.GetString(pso);
                    if (prName.Status != PromptStatus.OK) return;

                    string newName = prName.StringResult.Trim();
                    if (string.IsNullOrEmpty(newName) || newName.Equals(originalName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception("Invalid or duplicate name.");
                    }

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (bt.Has(newName))
                    {
                        throw new Exception($"A block named '{newName}' already exists!");
                    }

                    PromptKeywordOptions pko = new PromptKeywordOptions("\nChoose action [Clone/Rename] <Clone>: ");
                    pko.Keywords.Add("Clone");
                    pko.Keywords.Add("Rename");
                    pko.Keywords.Default = "Clone"; 
                    pko.AllowNone = true;

                    PromptResult prAction = ed.GetKeywords(pko);
                    if (prAction.Status != PromptStatus.OK) return;

                    bool isClone = (prAction.StringResult == "Clone");

                    if (isClone)
                    {
                        bt.UpgradeOpen();
                        BlockTableRecord oldBtr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead);

                        BlockTableRecord newBtr = new BlockTableRecord();
                        newBtr.Name = newName;
                        newBtr.Origin = oldBtr.Origin;
                        bt.Add(newBtr);
                        tr.AddNewlyCreatedDBObject(newBtr, true);

                        ObjectIdCollection ids = new ObjectIdCollection();
                        foreach (ObjectId id in oldBtr) { ids.Add(id); }
                        
                        if (ids.Count > 0)
                        {
                            IdMapping mapping = new IdMapping();
                            db.DeepCloneObjects(ids, newBtr.ObjectId, mapping, false);
                        }

                        UpdateOrAddInternalMText(tr, db, newBtr, newName);

                        BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        BlockReference newBlkRef = new BlockReference(blkRef.Position, newBtr.ObjectId)
                        {
                            ScaleFactors = blkRef.ScaleFactors,
                            Rotation = blkRef.Rotation,
                            Layer = blkRef.Layer,
                            Color = blkRef.Color
                        };

                        currentSpace.AppendEntity(newBlkRef);
                        tr.AddNewlyCreatedDBObject(newBlkRef, true);

                        blkRef.UpgradeOpen();
                        blkRef.Erase(); 

                        ed.WriteMessage($"\nSuccess: Cloned and replaced as '{newName}'.");
                    }
                    else
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForWrite);
                        btr.Name = newName;
                        UpdateOrAddInternalMText(tr, db, btr, newName);
                        ed.WriteMessage($"\nSuccess: Definition renamed to '{newName}'. All instances updated.");
                    }

                    tr.Commit();
                    Debug.WriteLine($"{LOG_PREFIX} Rename/Clone THÀNH CÔNG.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} LỖI Rename/Clone: {ex.Message}");
                    throw;
                }
            }
        }

        private void UpdateOrAddInternalMText(Transaction tr, Database db, BlockTableRecord btr, string newBlockName)
        {
            bool textFound = false;

            foreach (ObjectId entId in btr)
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                
                if (ent is DBText dbText)
                {
                    dbText.UpgradeOpen();
                    dbText.TextString = newBlockName;
                    textFound = true;
                }
                else if (ent is MText mText)
                {
                    mText.UpgradeOpen();
                    mText.Contents = newBlockName;
                    textFound = true;
                }
            }

            if (!textFound)
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                string reqLayer = "Mechanical-AM_9";

                if (!lt.Has(reqLayer))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord newLtr = new LayerTableRecord
                    {
                        Name = reqLayer,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7) 
                    };
                    lt.Add(newLtr);
                    tr.AddNewlyCreatedDBObject(newLtr, true);
                }

                MText newMText = new MText();
                newMText.SetDatabaseDefaults();
                newMText.Location = new Point3d(0, -15, 0); 
                newMText.TextHeight = 10;
                newMText.Contents = newBlockName;
                newMText.Layer = reqLayer;
                newMText.Attachment = AttachmentPoint.BottomLeft;

                btr.UpgradeOpen();
                btr.AppendEntity(newMText);
                tr.AddNewlyCreatedDBObject(newMText, true);
            }
        }
    }
}