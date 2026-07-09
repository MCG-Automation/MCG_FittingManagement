using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Utilities.FittingManagement
{
    /// <summary>
    /// Chứa các hàm tiện ích xử lý Block, Layer, Attribute tĩnh.
    /// Hoàn toàn KHÔNG chứa logic tương tác UI hay Editor (Prompt).
    /// </summary>
    public static class FittingBlockUtility
    {
        /// <summary>
        /// Kiểm tra và tạo Layer nếu chưa tồn tại.
        /// </summary>
        public static void CheckAndCreateLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        /// <summary>
        /// Thêm định nghĩa Attribute (tàng hình) vào Block Table Record.
        /// </summary>
        public static void AddAttributeDef(BlockTableRecord btr, Transaction tr, string tag, string val, string prompt, bool inv)
        {
            AttributeDefinition att = new AttributeDefinition
            {
                Position = new Point3d(0, 0, 0),
                Tag = tag,
                TextString = val ?? "",
                Prompt = prompt,
                Invisible = inv,
                Height = 2.5
            };
            btr.AppendEntity(att);
            tr.AddNewlyCreatedDBObject(att, true);
        }

        /// <summary>
        /// Chèn Block Reference vào ModelSpace và gán các Attribute từ định nghĩa.
        /// </summary>
        public static void InsertBlockReference(Database db, Transaction tr, ObjectId btrId, Point3d pos)
        {
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            BlockReference br = new BlockReference(pos, btrId);
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent is AttributeDefinition ad)
                {
                    AttributeReference ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, br.BlockTransform);
                    br.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                }
            }
        }

        /// <summary>
        /// Lấy tên thật của Block (Giải quyết vấn đề tên giả của Dynamic Block).
        /// </summary>
        public static string GetEffectiveName(Transaction tr, BlockReference blk)
        {
            if (blk.IsDynamicBlock)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            return blk.Name;
        }

        /// <summary>
        /// Nhúng/cập nhật catalog properties (bao gồm POS_NUM) vào AttributeDefinitions của Block Table Record.
        /// Gọi trong transaction với btr đã mở ForWrite. Không đụng tới VIEW_NAME (thuộc về identity
        /// cố định của block/view, không đổi theo catalog nên Sync không ghi đè).
        /// </summary>
        public static void EmbedCatalogProperties(BlockTableRecord btr, Transaction tr, CatalogItem item)
        {
            UpsertAttributeDefs(btr, tr, BuildCatalogPropDict(item));
        }

        /// <summary>
        /// Nhúng đầy đủ 10 attribute chuẩn (giống hệt tag Inventor import: PART_NUMBER, DESCRIPTION,
        /// MATERIAL, MASS, REVISION, DESIGNER, TITLE, BOM_TYPE, POS_NUM, VIEW_NAME) vào BTR.
        /// Dùng khi "Add from CAD" để block tạo theo cách này có attribute tương đương block Inventor.
        /// Upsert an toàn — cập nhật nếu tag đã tồn tại (vd POS_NUM có thể đã được thêm lúc Insert trước đó).
        /// </summary>
        public static void EmbedFullBimAttributes(BlockTableRecord btr, Transaction tr, CatalogItem item, string viewName)
        {
            var props = BuildCatalogPropDict(item);
            props["VIEW_NAME"] = viewName ?? "";
            UpsertAttributeDefs(btr, tr, props);
        }

        /// <summary>Cập nhật AttributeDefinition đã tồn tại (theo Tag, case-insensitive) hoặc thêm mới nếu chưa có.</summary>
        private static void UpsertAttributeDefs(BlockTableRecord btr, Transaction tr, Dictionary<string, string> props)
        {
            // Thu thập AttributeDefinition hiện có trong BTR
            var existing = new Dictionary<string, AttributeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad)
                    existing[ad.Tag] = ad;
            }

            foreach (var kvp in props)
            {
                if (existing.TryGetValue(kvp.Key, out AttributeDefinition def))
                {
                    def.UpgradeOpen();
                    def.TextString = kvp.Value;
                }
                else
                {
                    AddAttributeDef(btr, tr, kvp.Key, kvp.Value, kvp.Key, true);
                }
            }
        }

        /// <summary>
        /// Thêm (hoặc cập nhật nếu đã có sẵn trên layer chỉ định) MText label hiển thị tên block —
        /// quy ước layer "Mechanical-AM_9". Dùng khi tạo block mới (Inventor import) hoặc khi Rename Block.
        /// </summary>
        public static void AddNameLabelText(BlockTableRecord btr, Transaction tr, string labelText, string layerName)
        {
            MText newMText = new MText();
            newMText.SetDatabaseDefaults();
            newMText.Location = new Point3d(0, -15, 0);
            newMText.TextHeight = 10;
            newMText.Contents = labelText ?? "";
            newMText.Layer = layerName;
            newMText.Attachment = AttachmentPoint.BottomLeft;

            btr.UpgradeOpen();
            btr.AppendEntity(newMText);
            tr.AddNewlyCreatedDBObject(newMText, true);
        }

        /// <summary>
        /// Propagate catalog properties xuống tất cả INSERT instances của block trong drawing.
        /// Scan toàn bộ BlockTable (model space, nested blocks...). Instance cũ đã tồn tại trước khi
        /// tag này được thêm vào AttributeDefinition (vd block insert từ lâu, hoặc EmbedCatalogProperties
        /// vừa thêm tag mới) sẽ KHÔNG có AttributeReference tương ứng — AutoCAD không tự đồng bộ instance
        /// khi definition đổi (tương tự lý do lệnh ATTSYNC tồn tại). Nên với các tag còn thiếu trên từng
        /// instance, ta tự tạo mới AttributeReference từ AttributeDefinition gốc thay vì chỉ update cái đã có.
        /// Trả về số AttributeReference đã cập nhật + tạo mới.
        /// </summary>
        public static int SyncAttributeReferences(Database db, Transaction tr, string blockName, CatalogItem item)
        {
            var props = BuildCatalogPropDict(item);
            int count = 0;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName)) return count;

            // AttributeDefinition gốc (theo tag) từ definition BTR — dùng để tạo AttributeReference mới
            // cho instance nào còn thiếu tag đó.
            BlockTableRecord defBtr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);
            var attDefs = new Dictionary<string, AttributeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId defId in defBtr)
            {
                if (tr.GetObject(defId, OpenMode.ForRead) is AttributeDefinition ad && props.ContainsKey(ad.Tag))
                    attDefs[ad.Tag] = ad;
            }

            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId entId in btr)
                {
                    if (!(tr.GetObject(entId, OpenMode.ForRead) is BlockReference br)) continue;
                    if (!GetEffectiveName(tr, br).Equals(blockName, StringComparison.OrdinalIgnoreCase)) continue;

                    // 1. Cập nhật AttributeReference đã tồn tại trên instance
                    var existingTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                        if (attRef == null) continue;
                        existingTags.Add(attRef.Tag);
                        if (!props.TryGetValue(attRef.Tag, out string newVal)) continue;
                        attRef.UpgradeOpen();
                        attRef.TextString = newVal;
                        count++;
                    }

                    // 2. Tag nào chưa có trên instance này (block cũ, tag mới thêm sau) → tạo mới
                    foreach (var kvp in attDefs)
                    {
                        if (existingTags.Contains(kvp.Key)) continue;
                        AttributeReference ar = new AttributeReference();
                        ar.SetAttributeFromBlock(kvp.Value, br.BlockTransform);
                        ar.TextString = props[kvp.Key];
                        br.UpgradeOpen();
                        br.AttributeCollection.AppendAttribute(ar);
                        tr.AddNewlyCreatedDBObject(ar, true);
                        count++;
                    }
                }
            }
            return count;
        }

        // Props chuẩn nhúng vào block: khớp CHÍNH XÁC tên tag Inventor import dùng
        // (PART_NUMBER/DESCRIPTION/TITLE...) — trước đây dùng tag khác (PART_ID/XCLS/DESCR) gây
        // mismatch: Sync to Drawing tạo attribute trùng thay vì update tag đã có trên block Inventor.
        // POS_NUM nằm trong dict này vì Item Library workflow là Auto-Assign Pos (ghi ProjectPosNum
        // xuống project catalog) rồi Sync — Sync phải đẩy được giá trị Pos Num mới xuống drawing.
        private static Dictionary<string, string> BuildCatalogPropDict(CatalogItem item)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PART_NUMBER"] = item.PartNumber  ?? "",
                ["DESCRIPTION"] = item.Description ?? "",
                ["MATERIAL"]    = item.Material    ?? "",
                ["MASS"]        = item.Mass        ?? "0",
                ["REVISION"]    = item.Revision    ?? "",
                ["DESIGNER"]    = item.Designer    ?? "",
                ["TITLE"]       = item.Title       ?? "",
                ["BOM_TYPE"]    = item.BomType     ?? "",
                ["UOM"]         = item.UoM         ?? "pcs",
                ["POS_NUM"]     = item.ProjectPosNum ?? "",
            };
        }

        /// <summary>
        /// Sắp xếp danh sách Block theo không gian (Từ trên xuống dưới, trái qua phải).
        /// Sử dụng tâm Bounding Box để tính toán nhóm.
        /// </summary>
        public static List<BlockReference> SpatialSortByBoundingBox(List<BlockReference> blocks)
        {
            if (blocks == null || blocks.Count == 0) return new List<BlockReference>();

            // 1. Tính toán điểm tâm của hình bao (Bounding Box Center)
            var blockWithCenters = blocks.Select(b => 
            {
                Point3d centerPt = b.Position; // Fallback
                if (b.Bounds.HasValue)
                {
                    Extents3d ext = b.Bounds.Value;
                    centerPt = new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                        (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0
                    );
                }
                return new { Block = b, Center = centerPt };
            }).ToList();

            // 2. Sắp xếp sơ bộ theo chiều Y giảm dần
            var sortedByY = blockWithCenters.OrderByDescending(item => item.Center.Y).ToList();
            
            List<BlockReference> finalSorted = new List<BlockReference>();
            var currentGroup = new[] { sortedByY[0] }.ToList(); 
            
            double tolerance = 10.0; 
            double currentY = sortedByY[0].Center.Y;

            // 3. Phân nhóm theo hàng (Row) và sắp xếp trong từng hàng theo chiều X
            for (int i = 1; i < sortedByY.Count; i++)
            {
                if (Math.Abs(currentY - sortedByY[i].Center.Y) <= tolerance)
                {
                    currentGroup.Add(sortedByY[i]);
                }
                else
                {
                    finalSorted.AddRange(currentGroup.OrderBy(item => item.Center.X).Select(item => item.Block));
                    currentGroup.Clear();
                    currentGroup.Add(sortedByY[i]);
                    currentY = sortedByY[i].Center.Y;
                }
            }
            if (currentGroup.Count > 0)
            {
                finalSorted.AddRange(currentGroup.OrderBy(item => item.Center.X).Select(item => item.Block));
            }
            
            return finalSorted;
        }
    }
}