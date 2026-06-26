using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Interop;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using MCG_FittingManagement.Views.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        public void InteractiveBlockRenameClone()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu InteractiveBlockRenameClone...");
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db  = doc.Database;
            Editor   ed  = doc.Editor;

            // Phase 1 — Quét chọn nhiều block (ngoài lock và transaction)
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding  = "\nSelect blocks to rename: ";
            pso.MessageForRemoval = "\nRemove from selection: ";
            TypedValue[]    filterList = { new TypedValue((int)DxfCode.Start, "INSERT") };
            SelectionFilter filter     = new SelectionFilter(filterList);
            PromptSelectionResult psr  = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;

            // Phase 2 — Thu thập tên định nghĩa duy nhất → ObjectId các instance tương ứng
            var definitionMap = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    BlockReference blkRef = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (blkRef == null) continue;

                    string name = blkRef.IsDynamicBlock
                        ? ((BlockTableRecord)tr.GetObject(blkRef.DynamicBlockTableRecord, OpenMode.ForRead)).Name
                        : blkRef.Name;

                    if (!definitionMap.ContainsKey(name))
                        definitionMap[name] = new List<ObjectId>();
                    definitionMap[name].Add(so.ObjectId);
                }
            }
            if (definitionMap.Count == 0) return;

            // Phase 3 — Hiển thị dialog, nhận OldPattern / NewPattern từ user
            var dialog = new BlockRenameDialog(definitionMap.Keys);
            // Set owner = AutoCAD main window → dialog nằm trên AutoCAD, không chèn app khác
            new WindowInteropHelper(dialog).Owner = Application.MainWindow.Handle;
            if (dialog.ShowDialog() != true) return;

            string oldPattern = dialog.OldPattern;
            string newPattern = dialog.NewPattern;
            bool   isClone    = dialog.IsRenameCreateNew;

            // Resolve danh sách cặp (origName → renamedName)
            List<(string orig, string renamed)> pairs;
            if (oldPattern.Contains("*"))
            {
                pairs = ResolveWildcardMatches(oldPattern, newPattern, definitionMap.Keys);
                if (pairs.Count == 0) return;
            }
            else
            {
                if (newPattern.Equals(oldPattern, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("New name is the same as the original.");
                pairs = new List<(string, string)> { (oldPattern, newPattern) };
            }

            // Phase 4 — Ghi với document lock (bắt buộc khi gọi từ PaletteSet)
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                    // Validate tất cả cặp trước khi apply — all-or-nothing
                    foreach (var (orig, renamed) in pairs)
                    {
                        if (!bt.Has(orig))
                            throw new Exception($"Block definition '{orig}' not found.");
                        if (bt.Has(renamed))
                            throw new Exception($"A block named '{renamed}' already exists!");
                    }

                    if (isClone)
                        ApplyClone(tr, db, ed, bt, pairs, definitionMap);
                    else
                        ApplyRename(tr, db, ed, bt, pairs);

                    tr.Commit();
                    Debug.WriteLine($"{LOG_PREFIX} Rename/Clone THÀNH CÔNG. {pairs.Count} pair(s).");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"{LOG_PREFIX} LỖI Rename/Clone: {ex.Message}");
                    Debug.WriteLine($"{LOG_PREFIX} Stack trace:\n{ex.StackTrace}");
                    throw;
                }
            }
        }

        // Rename Create New: tạo definition mới, thay các instance đã chọn
        private void ApplyClone(
            Transaction tr, Database db, Editor ed,
            BlockTable bt,
            List<(string orig, string renamed)> pairs,
            Dictionary<string, List<ObjectId>> definitionMap)
        {
            bt.UpgradeOpen();
            foreach (var (orig, renamed) in pairs)
            {
                BlockTableRecord oldBtr = (BlockTableRecord)tr.GetObject(bt[orig], OpenMode.ForRead);
                BlockTableRecord newBtr = new BlockTableRecord { Name = renamed, Origin = oldBtr.Origin };
                bt.Add(newBtr);
                tr.AddNewlyCreatedDBObject(newBtr, true);

                // Deep-clone toàn bộ geometry từ definition gốc sang definition mới
                ObjectIdCollection ids = new ObjectIdCollection();
                foreach (ObjectId id in oldBtr) ids.Add(id);
                if (ids.Count > 0)
                {
                    IdMapping mapping = new IdMapping();
                    db.DeepCloneObjects(ids, newBtr.ObjectId, mapping, false);
                }
                UpdateOrAddInternalMText(tr, db, newBtr, renamed);

                // Thay thế các instance được chọn bằng definition mới
                if (!definitionMap.TryGetValue(orig, out List<ObjectId> selectedRefs)) continue;
                foreach (ObjectId refId in selectedRefs)
                {
                    BlockReference oldRef = tr.GetObject(refId, OpenMode.ForRead) as BlockReference;
                    if (oldRef == null) continue;

                    BlockTableRecord ownerSpace = (BlockTableRecord)tr.GetObject(oldRef.OwnerId, OpenMode.ForWrite);
                    BlockReference newRef = new BlockReference(oldRef.Position, newBtr.ObjectId)
                    {
                        ScaleFactors = oldRef.ScaleFactors,
                        Rotation     = oldRef.Rotation,
                        Layer        = oldRef.Layer,
                        Color        = oldRef.Color
                    };
                    ownerSpace.AppendEntity(newRef);
                    tr.AddNewlyCreatedDBObject(newRef, true);

                    oldRef.UpgradeOpen();
                    oldRef.Erase();
                }
                ed.WriteMessage($"\n'{orig}' → '{renamed}': {selectedRefs.Count} instance(s) replaced.");
            }
        }

        // Rename: đổi tên definition — toàn bộ instance trong bản vẽ cập nhật tự động
        private void ApplyRename(
            Transaction tr, Database db, Editor ed,
            BlockTable bt,
            List<(string orig, string renamed)> pairs)
        {
            foreach (var (orig, renamed) in pairs)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[orig], OpenMode.ForWrite);
                btr.Name = renamed;
                UpdateOrAddInternalMText(tr, db, btr, renamed);
                ed.WriteMessage($"\n'{orig}' renamed to '{renamed}'.");
            }
        }

        /// <summary>
        /// Tìm tất cả tên trong candidateNames khớp oldPattern (có 1 dấu *),
        /// tính tên mới bằng cách thay phần được capture vào newPattern.
        /// Ví dụ: old="MCG_*_01", new="NEW_*_01", name="MCG_Valve_01" → capture="Valve" → "NEW_Valve_01"
        /// </summary>
        private static List<(string orig, string renamed)> ResolveWildcardMatches(
            string oldPattern, string newPattern, IEnumerable<string> candidateNames)
        {
            int    starIdx = oldPattern.IndexOf('*');
            string prefix  = oldPattern.Substring(0, starIdx);
            string suffix  = oldPattern.Substring(starIdx + 1);

            var results = new List<(string, string)>();
            foreach (string name in candidateNames)
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Length < prefix.Length + suffix.Length) continue;

                string captured = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                string renamed  = newPattern.Replace("*", captured);
                results.Add((name, renamed));
            }
            return results;
        }

        private void UpdateOrAddInternalMText(Transaction tr, Database db, BlockTableRecord btr, string newBlockName)
        {
            bool textFound = false;

            foreach (ObjectId entId in btr)
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;

                // Chỉ xử lý text trên layer dành riêng cho name label
                if (ent.Layer != "Mechanical-AM_9") continue;

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
                LayerTable lt      = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                string     reqLayer = "Mechanical-AM_9";

                if (!lt.Has(reqLayer))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord newLtr = new LayerTableRecord
                    {
                        Name  = reqLayer,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7)
                    };
                    lt.Add(newLtr);
                    tr.AddNewlyCreatedDBObject(newLtr, true);
                }

                MText newMText = new MText();
                newMText.SetDatabaseDefaults();
                newMText.Location   = new Point3d(0, -15, 0);
                newMText.TextHeight = 10;
                newMText.Contents   = newBlockName;
                newMText.Layer      = reqLayer;
                newMText.Attachment = AttachmentPoint.BottomLeft;

                btr.UpgradeOpen();
                btr.AppendEntity(newMText);
                tr.AddNewlyCreatedDBObject(newMText, true);
            }
        }
    }
}
