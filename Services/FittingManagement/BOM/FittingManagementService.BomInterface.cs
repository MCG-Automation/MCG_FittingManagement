using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        // ====================================================================
        // 2. QUÉT BOM CHO INTERFACE (DETAIL/HULL)
        // ====================================================================
        public List<BomHarvestRecord> HarvestInterfaceBom()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu HarvestInterfaceBom...");
            List<BomHarvestRecord> rawResults = new List<BomHarvestRecord>();
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor ed = doc.Editor;

                List<CatalogItem> masterCatalog = GetMasterCatalogItems();

                PromptSelectionOptions selOpt = new PromptSelectionOptions();
                selOpt.MessageForAdding = "\nSelect A1 Frames to scan: ";
                TypedValue[] filter = new TypedValue[] { new TypedValue((int)DxfCode.Start, "INSERT"), new TypedValue((int)DxfCode.BlockName, "`*U*,A1") };
                PromptSelectionResult selRes = ed.GetSelection(selOpt, new SelectionFilter(filter));
                
                if (selRes.Status != PromptStatus.OK) return rawResults;

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord currentSpace = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                        
                        List<BlockReference> allSpaceBlocks = new List<BlockReference>();
                        List<Entity> allSpaceGeometries = new List<Entity>();

                        foreach (ObjectId id in currentSpace)
                        {
                            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (ent is BlockReference b) allSpaceBlocks.Add(b);
                            else if (ent is Polyline || ent is Line || ent is Circle || ent is Arc) allSpaceGeometries.Add(ent);
                        }

                        int a1Counter = 1;
                        foreach (SelectedObject selObj in selRes.Value)
                        {
                            BlockReference a1Blk = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                            if (a1Blk == null || !a1Blk.Bounds.HasValue) continue;

                            string a1Name = FittingBlockUtility.GetEffectiveName(tr, a1Blk);
                            if (!a1Name.Equals("A1", StringComparison.OrdinalIgnoreCase)) continue;

                            Extents3d a1Ext = a1Blk.GeometricExtents;
                            string detailName = GetAttributeValue(tr, a1Blk, "VIEW_NAME");
                            if (string.IsNullOrEmpty(detailName)) detailName = GetAttributeValue(tr, a1Blk, "TITLE");
                            if (string.IsNullOrEmpty(detailName)) detailName = $"Hull {a1Counter}";

                            foreach (BlockReference innerBlk in allSpaceBlocks)
                            {
                                if (innerBlk.ObjectId == a1Blk.ObjectId) continue;
                                if (IsPointInsideExtents(innerBlk.Position, a1Ext))
                                    ExtractFittingsFromBlock(tr, innerBlk, detailName, 1, masterCatalog, rawResults);
                            }

                            ExtractGeometricItems(tr, allSpaceGeometries, a1Ext, detailName, masterCatalog, rawResults);
                            a1Counter++;
                        }
                        tr.Commit();
                    }
                }

                return ConsolidateRawResults(rawResults);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI HarvestInterfaceBom: {ex.Message}");
                throw;
            }
        }

        private void ExtractFittingsFromBlock(Transaction tr, BlockReference blkRef, string detailName, int multiplier, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            string blkName = FittingBlockUtility.GetEffectiveName(tr, blkRef);
            if (blkName.Equals("A1", StringComparison.OrdinalIgnoreCase) || blkName.Equals("CAS_HEAD", StringComparison.OrdinalIgnoreCase)) return;

            if (blkName.IndexOf("CAS-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string vaultName = CleanVaultName(blkName);
                var mainCatItem = catalog.FirstOrDefault(c => c.PartNumber == vaultName || (c.BlockName != null && c.BlockName.Split(';').Contains(blkName, StringComparer.OrdinalIgnoreCase)));

                // C: dùng catalog PartNumber làm nguồn truth; fallback về vaultName nếu chưa có trong catalog
                string effectivePartId = mainCatItem?.PartNumber ?? vaultName;

                results.Add(new BomHarvestRecord {
                    PanelName = detailName, VaultName = effectivePartId, ParentBlockName = blkName,
                    Quantity = multiplier, UoM = "pcs", PartId = effectivePartId,
                    Description = mainCatItem != null && !string.IsNullOrEmpty(mainCatItem.Description) ? mainCatItem.Description : "Hull Fitting",
                    XClass = mainCatItem?.Title ?? "", ProjectPosNum = mainCatItem?.ProjectPosNum ?? "",
                    IsAccessory = false, ParentPartId = "",
                    InstanceHandles = new List<long> { blkRef.ObjectId.Handle.Value }
                });

                if (mainCatItem != null && mainCatItem.Accessories != null && mainCatItem.Accessories.Count > 0)
                {
                    foreach (var acc in mainCatItem.Accessories)
                    {
                        var accCatItem = catalog.FirstOrDefault(c => c.PartNumber == acc.PartId);
                        results.Add(new BomHarvestRecord {
                            PanelName = detailName, VaultName = acc.PartId, ParentBlockName = blkName,
                            Quantity = acc.Quantity * multiplier, UoM = accCatItem?.UoM ?? "pcs", PartId = acc.PartId,
                            Description = accCatItem != null && !string.IsNullOrEmpty(accCatItem.Description) ? accCatItem.Description : "Accessory",
                            XClass = accCatItem?.Title ?? "Accessory", ProjectPosNum = accCatItem?.ProjectPosNum ?? "",
                            IsAccessory = true, ParentPartId = effectivePartId,
                            InstanceHandles = new List<long> { blkRef.ObjectId.Handle.Value }
                        });
                    }
                }
            }

            if (!blkRef.IsDynamicBlock)
            {
                BlockTableRecord btr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                foreach (ObjectId childId in btr)
                {
                    if (tr.GetObject(childId, OpenMode.ForRead) is BlockReference childBlk)
                    {
                        ExtractFittingsFromBlock(tr, childBlk, detailName, multiplier * 1, catalog, results);
                    }
                }
            }
        }

        private void ExtractGeometricItems(Transaction tr, List<Entity> geometries, Extents3d a1Ext, string detailName, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            var geoRules = catalog.Where(c => c.EntityType != "Block" && !string.IsNullOrEmpty(c.TriggerLayer)).ToList();
            if (geoRules.Count == 0) return;

            foreach (var ent in geometries)
            {
                Point3d centerPt = GetEntityCenter(ent);
                if (!IsPointInsideExtents(centerPt, a1Ext)) continue;

                string entType = ent.GetType().Name;
                string entLayer = ent.Layer;

                foreach (var rule in geoRules)
                {
                    if (rule.EntityType == entType && rule.TriggerLayer == entLayer)
                    {
                        double qty = 1.0;
                        if (rule.UoM == "m") 
                        {
                            if (ent is Polyline pl) qty = pl.Length / 1000.0;
                            else if (ent is Line ln) qty = ln.Length / 1000.0;
                            else if (ent is Arc arc) qty = arc.Length / 1000.0;
                        }

                        results.Add(new BomHarvestRecord {
                            PanelName = detailName, VaultName = rule.PartNumber, ParentBlockName = "Linear Item",
                            Quantity = (int)Math.Ceiling(qty), UoM = rule.UoM, PartId = rule.PartNumber,
                            Description = string.IsNullOrEmpty(rule.Description) ? "Virtual Item" : rule.Description,
                            XClass = rule.Title ?? "", ProjectPosNum = rule.ProjectPosNum ?? "",
                            IsAccessory = false, ParentPartId = "",
                            InstanceHandles = new List<long> { ent.ObjectId.Handle.Value }
                        });
                        break; 
                    }
                }
            }
        }
    }
}