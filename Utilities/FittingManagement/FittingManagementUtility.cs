using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

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