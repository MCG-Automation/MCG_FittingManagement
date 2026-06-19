using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using MCG_FittingManagement.Models.FittingManagement;
using MCG_FittingManagement.Utilities.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Chuyên xử lý việc quét chọn các đối tượng hình học (Line, Polyline, Block...)
    /// trên mặt bằng CAD để biến chúng thành Fitting Ảo (Virtual Items).
    /// </summary>
    public partial class FittingManagementService
    {
        public CatalogItem PickGeometricFeatureFromCad()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu pick Geometric Feature (Virtual Item)...");
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;
                Database db = doc.Database;

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nSelect objects (Blocks, Lines, Polylines, Circles, Arcs) to define as a Fitting: ";
                PromptSelectionResult psr = ed.GetSelection(pso);

                if (psr.Status != PromptStatus.OK)
                {
                    Debug.WriteLine($"{LOG_PREFIX} Lệnh Pick bị hủy.");
                    return null;
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        try
                        {
                            CatalogItem draftItem = new CatalogItem();
                            List<string> collectedBlockNames = new List<string>();
                            Entity firstValidEnt = null;

                            ObjectId[] ids = psr.Value.GetObjectIds();
                            foreach (ObjectId id in ids)
                            {
                                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                if (ent == null) continue;

                                bool isValidObject = ent is Line || ent is Polyline || ent is Polyline2d || ent is Polyline3d || ent is Circle || ent is Arc || ent is BlockReference;
                                if (!isValidObject) continue;

                                if (firstValidEnt == null) firstValidEnt = ent;

                                if (ent is BlockReference blk)
                                {
                                    string blkName = FittingBlockUtility.GetEffectiveName(tr, blk);
                                    if (!collectedBlockNames.Contains(blkName)) collectedBlockNames.Add(blkName);
                                }
                            }

                            if (firstValidEnt == null)
                            {
                                throw new Exception("No valid geometric objects selected.");
                            }

                            draftItem.EntityType = collectedBlockNames.Count > 0 ? "Block" : firstValidEnt.GetType().Name;
                            draftItem.TriggerLayer = firstValidEnt.Layer;

                            if (firstValidEnt.Color.IsByLayer)
                            {
                                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                                if (lt.Has(firstValidEnt.Layer))
                                {
                                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[firstValidEnt.Layer], OpenMode.ForRead);
                                    draftItem.TriggerColor = $"ByLayer (Index: {ltr.Color.ColorIndex})";
                                }
                            }
                            else
                            {
                                draftItem.TriggerColor = $"Index: {firstValidEnt.ColorIndex}";
                            }

                            double width = 0;
                            if (collectedBlockNames.Count > 0)
                            {
                                draftItem.BlockName = string.Join(";", collectedBlockNames);
                                draftItem.UoM = "pcs";
                                var match = System.Text.RegularExpressions.Regex.Match(collectedBlockNames[0], @"(?i)CAS-\d{7}");
                                if (match.Success) draftItem.PartNumber = match.Value.ToUpper();
                            }
                            else if (firstValidEnt is Polyline pline)
                            {
                                try { width = pline.ConstantWidth; }
                                catch { if (pline.NumberOfVertices > 0) width = pline.GetStartWidthAt(0); }
                                draftItem.UoM = "m";
                            }
                            else if (firstValidEnt is Polyline2d pline2d)
                            {
                                try { width = pline2d.DefaultStartWidth; } catch { }
                                draftItem.UoM = "m";
                            }
                            else if (firstValidEnt is Line || firstValidEnt is Arc) { draftItem.UoM = "m"; }
                            else { draftItem.UoM = "pcs"; }

                            if (width > 0) draftItem.Description = $"[Width: {width}] ";
                            draftItem.BomType = "DETAIL";
                            if (string.IsNullOrEmpty(draftItem.PartNumber)) draftItem.PartNumber = "";
                            draftItem.Title = "";
                            draftItem.Mass = "0";

                            tr.Commit();
                            Debug.WriteLine($"{LOG_PREFIX} Pick Feature THÀNH CÔNG.");
                            return draftItem;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"{LOG_PREFIX} Transaction ABORT (PickFeature): {ex.Message}");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI PickGeometricFeatureFromCad: {ex.Message}");
                throw;
            }
        }
    }
}