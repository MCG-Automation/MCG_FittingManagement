using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        /// <summary>
        /// Đồng bộ định nghĩa Block giữa hai drawing đang mở: chọn block trong drawing hiện tại,
        /// pick 1 drawing khác đang mở làm source, dùng <c>WblockCloneObjects</c> với
        /// <c>DuplicateRecordCloning.Replace</c> để overwrite block table record.
        /// KHÔNG đụng tới file .dwg trong Master Library.
        /// </summary>
        public void RedefineBlocksFromOpenDrawing()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu RedefineBlocksFromOpenDrawing...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database destDb = doc.Database;
            Editor ed = doc.Editor;

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nSelect Blocks to Sync/Redefine: ";
            TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
            PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));
            
            if (psr.Status != PromptStatus.OK) return;

            HashSet<string> blockNamesToSync = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = destDb.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in psr.Value)
                {
                    BlockReference blkRef = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (blkRef != null)
                    {
                        string name = FittingBlockUtility.GetEffectiveName(tr, blkRef);
                        blockNamesToSync.Add(name);
                    }
                }
                tr.Commit();
            }

            if (blockNamesToSync.Count == 0) return;

            DocumentCollection docs = Application.DocumentManager;
            List<Document> availableDocs = new List<Document>();
            foreach (Document d in docs)
            {
                if (d != doc) availableDocs.Add(d);
            }

            if (availableDocs.Count == 0)
            {
                throw new Exception("No other drawings are currently open.\nPlease open the source drawing in another tab first.");
            }

            StringBuilder promptBuilder = new StringBuilder("\nEnter source drawing number: ");
            for (int i = 0; i < availableDocs.Count; i++)
            {
                promptBuilder.Append($"[{i + 1}: {Path.GetFileName(availableDocs[i].Name)}]  ");
            }

            PromptIntegerOptions pio = new PromptIntegerOptions(promptBuilder.ToString());
            pio.LowerLimit = 1;
            pio.UpperLimit = availableDocs.Count;
            PromptIntegerResult pir = ed.GetInteger(pio);

            if (pir.Status != PromptStatus.OK) return;

            Document sourceDoc = availableDocs[pir.Value - 1];
            Database sourceDb = sourceDoc.Database;
            int updatedCount = 0;

            using (DocumentLock destLock = doc.LockDocument())
            using (DocumentLock srcLock = sourceDoc.LockDocument())
            {
                try
                {
                    ObjectIdCollection sourceBlockIds = new ObjectIdCollection();

                    using (Transaction srcTr = sourceDb.TransactionManager.StartTransaction())
                    {
                        BlockTable srcBt = (BlockTable)srcTr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                        foreach (string bName in blockNamesToSync)
                        {
                            if (srcBt.Has(bName))
                            {
                                sourceBlockIds.Add(srcBt[bName]);
                                updatedCount++;
                            }
                            else
                            {
                                ed.WriteMessage($"\n[Warning] Block '{bName}' not found in source drawing.");
                            }
                        }
                        srcTr.Commit(); 
                    }

                    if (sourceBlockIds.Count > 0)
                    {
                        using (Transaction destTr = destDb.TransactionManager.StartTransaction())
                        {
                            IdMapping mapping = new IdMapping();
                            destDb.WblockCloneObjects(sourceBlockIds, destDb.BlockTableId, mapping, DuplicateRecordCloning.Replace, false);
                            destTr.Commit(); 
                        }
                        
                        ed.Regen();
                        ed.WriteMessage($"\nSuccessfully synced {updatedCount} block(s) from '{Path.GetFileName(sourceDoc.Name)}'!");
                        Debug.WriteLine($"{LOG_PREFIX} Redefine THÀNH CÔNG.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} LỖI RedefineBlocks: {ex.Message}");
                    throw;
                }
            }
        }
    }
}