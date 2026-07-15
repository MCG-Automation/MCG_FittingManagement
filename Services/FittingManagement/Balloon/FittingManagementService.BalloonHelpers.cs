using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        private class DiscoveredFitting
        {
            public string PosNum { get; set; }
            public Point3d ArrowPoint { get; set; }
        }

        // ====================================================================
        // HELPER FUNCTIONS (Nội bộ của Balloon Engine)
        // ====================================================================
        private void DiscoverFittings(Transaction tr, BlockReference blk, Matrix3d currentTransform, HashSet<string> balloonedPos, List<DiscoveredFitting> foundFittings)
        {
            string posNum = "";
            bool foundPos = false;
            
            if (blk.AttributeCollection != null)
            {
                foreach (ObjectId attId in blk.AttributeCollection)
                {
                    AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (attRef != null && attRef.Tag.Equals("POS_NUM", StringComparison.OrdinalIgnoreCase))
                    {
                        posNum = attRef.TextString;
                        foundPos = true;
                        break;
                    }
                }
            }

            if (foundPos && !string.IsNullOrWhiteSpace(posNum) && !balloonedPos.Contains(posNum))
            {
                Point3d arrowPoint = Point3d.Origin.TransformBy(currentTransform);
                foundFittings.Add(new DiscoveredFitting { PosNum = posNum, ArrowPoint = arrowPoint });
                balloonedPos.Add(posNum);
            }

            ObjectId btrId = blk.IsDynamicBlock ? blk.DynamicBlockTableRecord : blk.BlockTableRecord;
            BlockTableRecord btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
            
            foreach (ObjectId childId in btr)
            {
                BlockReference childBlk = tr.GetObject(childId, OpenMode.ForRead) as BlockReference;
                if (childBlk != null)
                {
                    Matrix3d nextTransform = currentTransform * childBlk.BlockTransform;
                    DiscoverFittings(tr, childBlk, nextTransform, balloonedPos, foundFittings);
                }
            }
        }

        private bool IsSlotOccupied(Point3d pt, List<Point3d> occupied, double minDistance)
        {
            foreach (var occ in occupied)
                if (pt.DistanceTo(occ) < minDistance) return true;
            return false;
        }

        private void DrawMagneticMLeader(Transaction tr, BlockTableRecord btrSpace, Database db, Point3d arrowPt, Point3d balloonPt, string rawPosNum, double scale)
        {
            string[] posNumbers = rawPosNum.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (posNumbers.Length == 0) return;

            Vector3d doglegDir = (balloonPt.X > arrowPt.X) ? Vector3d.XAxis : -Vector3d.XAxis;

            // Bắt buộc dùng Block + Circle theo style chuẩn — tự tạo _TagCircle nếu drawing chưa có.
            ObjectId tagCircleId = EnsureTagCircleBlock(db, tr);

            using (MLeader mleader = new MLeader())
            {
                mleader.SetDatabaseDefaults();
                mleader.Scale = scale;
                mleader.ArrowSize = 3.0;
                mleader.EnableDogleg = true;
                mleader.DoglegLength = 0.001;

                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has("Mechanical-AM_5")) mleader.Layer = "Mechanical-AM_5";

                // Mleader entity properties → ByLayer (Color/Linetype/LineWeight kế thừa từ Layer trên).
                mleader.ColorIndex = 256;                       // 256 = ByLayer
                mleader.Linetype = "ByLayer";
                mleader.LineWeight = LineWeight.ByLayer;

                int leaderIndex = mleader.AddLeader();
                int leaderLineIndex = mleader.AddLeaderLine(leaderIndex);
                mleader.AddFirstVertex(leaderLineIndex, arrowPt);
                mleader.AddLastVertex(leaderLineIndex, balloonPt);
                mleader.SetDogleg(leaderIndex, doglegDir);

                mleader.ContentType = ContentType.BlockContent;
                mleader.BlockContentId = tagCircleId;
                mleader.BlockConnectionType = BlockConnectionType.ConnectExtents;
                mleader.BlockPosition = balloonPt;
                // BlockScale = Scale (không hardcode 1.0) — khớp với stackedBlk bên dưới (đã dùng
                // ScaleFactors = new Scale3d(scale) từ trước), đảm bảo circle của balloon chính
                // (leaderIndex đầu) thật sự phóng theo tỉ lệ A1 giống các circle phụ khi có nhiều Pos Num.
                mleader.BlockScale = new Scale3d(mleader.Scale);
                mleader.BlockColor = Color.FromColorIndex(ColorMethod.ByLayer, 256);

                btrSpace.AppendEntity(mleader);
                tr.AddNewlyCreatedDBObject(mleader, true);

                SetBlockAttributeInternal(tr, tagCircleId, mleader, posNumbers[0]);
            }

            if (posNumbers.Length > 1)
            {
                double circleSpacing = 14.0 * scale;
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                bool hasMechLayer = lt.Has("Mechanical-AM_5");

                for (int i = 1; i < posNumbers.Length; i++)
                {
                    Point3d nextPt = balloonPt + doglegDir * (circleSpacing * i);
                    using (BlockReference stackedBlk = new BlockReference(nextPt, tagCircleId))
                    {
                        stackedBlk.ScaleFactors = new Scale3d(scale);
                        if (hasMechLayer) stackedBlk.Layer = "Mechanical-AM_5";

                        // Stacked block — ByLayer cho mọi visual property.
                        stackedBlk.ColorIndex = 256;
                        stackedBlk.Linetype = "ByLayer";
                        stackedBlk.LineWeight = LineWeight.ByLayer;

                        btrSpace.AppendEntity(stackedBlk);
                        tr.AddNewlyCreatedDBObject(stackedBlk, true);

                        InjectAttributeToBlockInternal(tr, stackedBlk, posNumbers[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Trả về ObjectId của block <c>_TagCircle</c>. Tạo nếu chưa có (Circle radius=1 + AttributeDefinition
        /// tag <c>TAGNUMBER</c> height=1 căn giữa). Sau đó normalize entities BÊN TRONG block về
        /// Layer="0" + Color ByLayer (self-heal cho block tạo ở session cũ chưa có ByLayer).
        /// </summary>
        private ObjectId EnsureTagCircleBlock(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            ObjectId btrId;
            if (bt.Has("_TagCircle"))
            {
                btrId = bt["_TagCircle"];
            }
            else
            {
                bt.UpgradeOpen();
                using (BlockTableRecord newBtr = new BlockTableRecord())
                {
                    newBtr.Name = "_TagCircle";
                    newBtr.Origin = Point3d.Origin;
                    newBtr.BlockScaling = BlockScaling.Uniform;

                    btrId = bt.Add(newBtr);
                    tr.AddNewlyCreatedDBObject(newBtr, true);

                    using (Circle circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 1.0))
                    {
                        circle.SetDatabaseDefaults();
                        newBtr.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);
                    }

                    using (AttributeDefinition attDef = new AttributeDefinition())
                    {
                        attDef.SetDatabaseDefaults();
                        attDef.Tag = "TAGNUMBER";
                        attDef.Prompt = "Tag number";
                        attDef.TextString = "";
                        attDef.Height = 1.0;
                        attDef.Justify = AttachmentPoint.MiddleCenter;
                        attDef.AlignmentPoint = Point3d.Origin;
                        attDef.Position = Point3d.Origin;
                        attDef.Invisible = false;
                        newBtr.AppendEntity(attDef);
                        tr.AddNewlyCreatedDBObject(attDef, true);
                    }
                    Debug.WriteLine($"{LOG_PREFIX} Đã tự tạo block _TagCircle (lần đầu drawing dùng MLeader-Circle).");
                }
            }

            // Normalize entities trong block — Layer="0" + Linetype/LineWeight ByLayer.
            // Color: text/MText → vàng (ColorIndex=2); Circle và entity khác → ByLayer.
            // Áp dụng cho cả block mới tạo và block cũ đã tồn tại (self-heal session cũ).
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId entId in btr)
            {
                Entity ent = tr.GetObject(entId, OpenMode.ForWrite) as Entity;
                if (ent == null) continue;
                try
                {
                    ent.Layer = "0";
                    ent.Linetype = "ByLayer";
                    ent.LineWeight = LineWeight.ByLayer;

                    // Text-like entities → màu vàng. Khác → ByLayer.
                    bool isTextLike = ent is AttributeDefinition || ent is MText || ent is DBText;
                    ent.ColorIndex = isTextLike ? 2 : 256;
                }
                catch (System.Exception exNorm)
                {
                    Debug.WriteLine($"{LOG_PREFIX} Normalize _TagCircle entity '{ent.GetType().Name}' fail (non-fatal): {exNorm.Message}");
                }
            }

            return btrId;
        }

        /// <summary>
        /// Tìm A1 BlockReference (KeepAsIs name == "A1") trong CurrentSpace có bbox chứa <paramref name="pt"/>,
        /// trả về <c>ScaleFactors.X</c> của nó. Trả null nếu không A1 nào chứa pt — caller dùng fallback.
        /// </summary>
        private double? ComputeA1Scale(Transaction tr, Database db, Point3d pt)
        {
            try
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference)))) continue;
                    BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    // BlockReference.Name resolve về tên gốc của dynamic block — không phải *Uxxx anonymous.
                    string name = br.Name;
                    if (!string.Equals(name, "A1", StringComparison.OrdinalIgnoreCase)) continue;

                    Extents3d ext;
                    try { ext = br.GeometricExtents; }
                    catch { continue; }

                    if (pt.X >= ext.MinPoint.X && pt.X <= ext.MaxPoint.X &&
                        pt.Y >= ext.MinPoint.Y && pt.Y <= ext.MaxPoint.Y)
                    {
                        double s = Math.Abs(br.ScaleFactors.X);
                        Debug.WriteLine($"{LOG_PREFIX} ComputeA1Scale: arrowPt={pt} → A1 '{name}' ScaleX={s:F3}.");
                        return s;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} ComputeA1Scale LỖI (non-fatal): {ex.Message}");
            }
            return null;
        }

        private void SetBlockAttributeInternal(Transaction tr, ObjectId blockId, MLeader leader, string value)
        {
            BlockTableRecord circleBtr = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
            ObjectId attDefId = ObjectId.Null;
            foreach (ObjectId id in circleBtr)
            {
                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(AttributeDefinition))))
                {
                    attDefId = id; break;
                }
            }

            if (attDefId != ObjectId.Null)
            {
                AttributeDefinition attDef = (AttributeDefinition)tr.GetObject(attDefId, OpenMode.ForRead);
                AttributeReference attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, Matrix3d.Identity); 
                attRef.TextString = value;
                leader.SetBlockAttribute(attDefId, attRef);
            }
        }

        private void InjectAttributeToBlockInternal(Transaction tr, BlockReference blkRef, string value)
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(AttributeDefinition))))
                {
                    AttributeDefinition attDef = (AttributeDefinition)tr.GetObject(id, OpenMode.ForRead);
                    using (AttributeReference attRef = new AttributeReference())
                    {
                        attRef.SetAttributeFromBlock(attDef, blkRef.BlockTransform);
                        attRef.TextString = value;
                        blkRef.AttributeCollection.AppendAttribute(attRef);
                        tr.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }
        }
    }
}