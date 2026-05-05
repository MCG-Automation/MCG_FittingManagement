using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCGCadPlugin.Utilities.FittingManagement;

namespace MCGCadPlugin.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        public void SmartReplaceBlocks()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu SmartReplaceBlocks...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (DocumentLock docLock = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        PromptSelectionOptions psoSrc = new PromptSelectionOptions();
                        psoSrc.MessageForAdding = "\nStep 1: Select SOURCE Blocks (Old): ";
                        TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                        PromptSelectionResult psrSrc = ed.GetSelection(psoSrc, new SelectionFilter(filter));
                        
                        if (psrSrc.Status != PromptStatus.OK) return;

                        PromptSelectionOptions psoTgt = new PromptSelectionOptions();
                        psoTgt.MessageForAdding = "\nStep 2: Select TARGET Blocks (New): ";
                        PromptSelectionResult psrTgt = ed.GetSelection(psoTgt, new SelectionFilter(filter));
                        
                        if (psrTgt.Status != PromptStatus.OK) return;

                        if (psrSrc.Value.Count != psrTgt.Value.Count)
                        {
                            throw new Exception($"Quantity mismatch! Source: {psrSrc.Value.Count}, Target: {psrTgt.Value.Count}");
                        }

                        List<BlockReference> srcBlocks = GetBlockReferences(tr, psrSrc.Value);
                        List<BlockReference> tgtBlocks = GetBlockReferences(tr, psrTgt.Value);

                        // Sử dụng Hàm Utility đã tách ở Bước 2
                        srcBlocks = FittingBlockUtility.SpatialSortByBoundingBox(srcBlocks);
                        tgtBlocks = FittingBlockUtility.SpatialSortByBoundingBox(tgtBlocks);

                        Dictionary<string, ObjectId> blockMap = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                        for (int i = 0; i < srcBlocks.Count; i++)
                        {
                            string srcName = FittingBlockUtility.GetEffectiveName(tr, srcBlocks[i]);
                            string tgtName = FittingBlockUtility.GetEffectiveName(tr, tgtBlocks[i]);

                            if (!srcName.Equals(tgtName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (!blockMap.ContainsKey(srcName) && bt.Has(tgtName))
                                {
                                    blockMap.Add(srcName, bt[tgtName]);
                                }
                            }
                        }

                        if (blockMap.Count == 0)
                        {
                            throw new Exception("No mapping rules created (all source and target names are identical).");
                        }

                        PromptSelectionOptions psoScope = new PromptSelectionOptions();
                        psoScope.MessageForAdding = "\nStep 3: Select blocks to replace (Press ENTER for GLOBAL replacement): ";
                        PromptSelectionResult psrScope = ed.GetSelection(psoScope, new SelectionFilter(filter));

                        int replacedCount = 0;

                        if (psrScope.Status == PromptStatus.Error || psrScope.Status == PromptStatus.Cancel || psrScope.Status == PromptStatus.None)
                        {
                            ed.WriteMessage("\nExecuting GLOBAL replacement...");
                            foreach (ObjectId btrId in bt)
                            {
                                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                                replacedCount += ReplaceBlocksInBTR(tr, btr, blockMap);
                            }
                        }
                        else if (psrScope.Status == PromptStatus.OK)
                        {
                            ed.WriteMessage("\nExecuting SELECTION replacement (Recursive)...");
                            List<BlockReference> scopeBlocks = GetBlockReferences(tr, psrScope.Value);
                            HashSet<ObjectId> processedDefs = new HashSet<ObjectId>();

                            foreach (var blkRef in scopeBlocks)
                            {
                                string currentName = FittingBlockUtility.GetEffectiveName(tr, blkRef);
                                if (blockMap.ContainsKey(currentName))
                                {
                                    blkRef.UpgradeOpen();
                                    blkRef.BlockTableRecord = blockMap[currentName]; 
                                    replacedCount++;
                                }

                                ObjectId defId = blkRef.IsDynamicBlock ? blkRef.DynamicBlockTableRecord : blkRef.BlockTableRecord;
                                replacedCount += RecursiveReplaceDef(tr, defId, blockMap, processedDefs);
                            }
                        }

                        tr.Commit();
                        ed.Regen();
                        ed.WriteMessage($"\nReplacement Complete! Changed {replacedCount} block reference(s).");
                        Debug.WriteLine($"{LOG_PREFIX} SmartReplaceBlocks THÀNH CÔNG.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{LOG_PREFIX} LỖI SmartReplaceBlocks: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        private List<BlockReference> GetBlockReferences(Transaction tr, SelectionSet sSet)
        {
            List<BlockReference> list = new List<BlockReference>();
            foreach (SelectedObject selObj in sSet)
            {
                BlockReference br = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br != null) list.Add(br);
            }
            return list;
        }

        private int ReplaceBlocksInBTR(Transaction tr, BlockTableRecord btr, Dictionary<string, ObjectId> blockMap)
        {
            int count = 0;
            btr.UpgradeOpen();
            foreach (ObjectId id in btr)
            {
                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br != null)
                {
                    string name = FittingBlockUtility.GetEffectiveName(tr, br);
                    if (blockMap.ContainsKey(name))
                    {
                        br.UpgradeOpen();
                        br.BlockTableRecord = blockMap[name];
                        count++;
                    }
                }
            }
            return count;
        }

        private int RecursiveReplaceDef(Transaction tr, ObjectId btrId, Dictionary<string, ObjectId> blockMap, HashSet<ObjectId> processed)
        {
            if (processed.Contains(btrId)) return 0;
            processed.Add(btrId);

            int count = 0;
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            
            foreach (ObjectId id in btr)
            {
                BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br != null)
                {
                    ObjectId childDefId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                    count += RecursiveReplaceDef(tr, childDefId, blockMap, processed);

                    string name = FittingBlockUtility.GetEffectiveName(tr, br);
                    if (blockMap.ContainsKey(name))
                    {
                        br.UpgradeOpen();
                        br.BlockTableRecord = blockMap[name];
                        count++;
                    }
                }
            }
            return count;
        }
    }
}