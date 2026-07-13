using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        // ====================================================================
        // 1. QUÉT BOM CHO STRUCTURE (PANEL)
        // ====================================================================
        public List<BomHarvestRecord> HarvestStructureBom()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu HarvestStructureBom...");
            List<BomHarvestRecord> rawRecords = new List<BomHarvestRecord>();
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                Editor ed = doc.Editor;

                List<CatalogItem> masterCatalog = GetMasterCatalogItems();

                using (DocumentLock docLock = doc.LockDocument())
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\nSelect Panel Block References to analyze: ";
                    TypedValue[] filter = { new TypedValue((int)DxfCode.Start, "INSERT") };
                    PromptSelectionResult psr = ed.GetSelection(pso, new SelectionFilter(filter));

                    if (psr.Status != PromptStatus.OK) return rawRecords;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject selObj in psr.Value)
                        {
                            BlockReference panelRef = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as BlockReference;
                            if (panelRef == null) continue;

                            string panelName = FittingBlockUtility.GetEffectiveName(tr, panelRef);
                            panelName = CleanPanelName(panelName);
                            ed.WriteMessage($"\n>>> PROCESSING PANEL: {panelName}");

                            BlockTableRecord btr = tr.GetObject(panelRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            ExtractStructureItemsRecursive(tr, btr, panelRef.ScaleFactors.X, panelName, masterCatalog, rawRecords);
                        }
                        tr.Commit();
                    }
                }

                var consolidated = ConsolidateRawResults(rawRecords);
                Debug.WriteLine($"{LOG_PREFIX} HarvestStructureBom THÀNH CÔNG. Tìm thấy {consolidated.Count} nhóm.");
                return consolidated;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI HarvestStructureBom: {ex.Message}");
                throw;
            }
        }

        private void ExtractStructureItemsRecursive(Transaction tr, BlockTableRecord btr, double currentScale, string panelName, List<CatalogItem> catalog, List<BomHarvestRecord> results)
        {
            var geoRules = catalog.Where(c => c.EntityType != "Block" && !string.IsNullOrEmpty(c.TriggerLayer)).ToList();

            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is BlockReference blkRef)
                {
                    if (blkRef.Layer == "Mechanical-AM_7") continue; 

                    string blkName = FittingBlockUtility.GetEffectiveName(tr, blkRef);

                    // Opt-in: chỉ đếm khi BOM_TYPE khớp ĐÚNG EQUIPMENT/PANEL — không còn dựa vào
                    // tên block chứa "CAS-" (naming convention dễ vỡ). Loại hẳn rủi ro double-count
                    // (1 block để trống BOM_TYPE mà vừa lồng trong Panel vừa nằm trong khung A1)
                    // và sửa luôn lỗ hổng ngược: block gắn đúng BOM_TYPE nhưng tên không có "CAS-"
                    // trước đây bị loại khỏi CẢ 2 BOM.
                    string bomType = GetAttributeValue(tr, blkRef, "BOM_TYPE").ToUpper();
                    if (bomType == "EQUIPMENT" || bomType == "PANEL")
                    {
                        string vaultName = CleanVaultName(blkName);
                        var mainCatItem = catalog.FirstOrDefault(c => c.PartNumber == vaultName || (c.BlockName != null && c.BlockName.Split(';').Contains(blkName, StringComparer.OrdinalIgnoreCase)));

                        // C: dùng catalog PartNumber làm nguồn truth; fallback về vaultName nếu chưa có trong catalog
                        string effectivePartId = mainCatItem?.PartNumber ?? vaultName;

                        results.Add(new BomHarvestRecord {
                            PanelName = panelName, VaultName = effectivePartId, ParentBlockName = blkName,
                            Quantity = 1, UoM = mainCatItem?.UoM ?? "pcs", PartId = effectivePartId,
                            Description = mainCatItem != null && !string.IsNullOrEmpty(mainCatItem.Description) ? mainCatItem.Description : "Harvested from CAD",
                            XClass = mainCatItem?.Title ?? "", ProjectPosNum = mainCatItem?.ProjectPosNum ?? "",
                            IsAccessory = false, ParentPartId = "",
                            InstanceHandles = new List<long> { blkRef.ObjectId.Handle.Value }
                        });

                        if (mainCatItem != null && mainCatItem.Accessories != null)
                        {
                            foreach (var acc in mainCatItem.Accessories)
                            {
                                var accCatItem = catalog.FirstOrDefault(c => c.PartNumber == acc.PartId);
                                results.Add(new BomHarvestRecord {
                                    PanelName = panelName, VaultName = acc.PartId, ParentBlockName = blkName,
                                    Quantity = acc.Quantity, UoM = accCatItem?.UoM ?? "pcs", PartId = acc.PartId,
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
                        double nextScale = Math.Abs(blkRef.ScaleFactors.X) * currentScale;
                        BlockTableRecord nestedBtr = tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        ExtractStructureItemsRecursive(tr, nestedBtr, nextScale, panelName, catalog, results);
                    }
                }
                else if (geoRules.Count > 0 && (ent is Polyline || ent is Line || ent is Arc || ent is Circle))
                {
                    string entType = ent.GetType().Name;
                    string entLayer = ent.Layer;

                    foreach (var rule in geoRules)
                    {
                        if (rule.EntityType == entType && rule.TriggerLayer == entLayer)
                        {
                            double qty = 1.0;
                            if (rule.UoM == "m")
                            {
                                if (ent is Polyline pl) qty = (pl.Length * currentScale) / 1000.0;
                                else if (ent is Line ln) qty = (ln.Length * currentScale) / 1000.0;
                                else if (ent is Arc arc) qty = (arc.Length * currentScale) / 1000.0;
                            }

                            results.Add(new BomHarvestRecord {
                                PanelName = panelName, VaultName = rule.PartNumber, ParentBlockName = "Linear Item",
                                Quantity = (int)Math.Ceiling(qty), UoM = rule.UoM, PartId = rule.PartNumber,
                                Description = !string.IsNullOrEmpty(rule.Description) ? rule.Description : "Virtual Item",
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
}