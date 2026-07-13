using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    public partial class FittingManagementService
    {
        // ====================================================================
        // 3. CÁC HÀM HELPER NỘI BỘ DÀNH RIÊNG CHO BOM HARVESTER
        // ====================================================================
        
        private List<BomHarvestRecord> ConsolidateRawResults(List<BomHarvestRecord> rawList)
        {
            return rawList
                .GroupBy(r => new { r.PanelName, r.VaultName, r.ParentBlockName, r.UoM, r.IsAccessory, r.ParentPartId })
                .Select(g => new BomHarvestRecord
                {
                    PanelName = g.Key.PanelName, VaultName = g.Key.VaultName, ParentBlockName = g.Key.ParentBlockName,
                    UoM = g.Key.UoM, PartId = g.First().PartId, Description = g.First().Description,
                    XClass = g.First().XClass, ProjectPosNum = g.First().ProjectPosNum,
                    IsAccessory = g.Key.IsAccessory, ParentPartId = g.Key.ParentPartId,
                    IsPlanView = g.First().IsPlanView, CountPlanViewOnly = g.First().CountPlanViewOnly,
                    Quantity = g.Sum(r => r.Quantity),
                    InstanceHandles = g.SelectMany(r => r.InstanceHandles).ToList()
                }).ToList();
        }

        private string CleanPanelName(string fullName)
        {
            string clean = fullName.Trim();
            if (clean.StartsWith("New ", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(4).Trim();
            if (clean.StartsWith("T.", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(2).Trim();
            if (clean.EndsWith("_Assy", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 5);
            return clean;
        }

        private string CleanVaultName(string fullName)
        {
            var match = Regex.Match(fullName, @"(?i)CAS-\d{7}");
            return match.Success ? match.Value.ToUpper() : fullName;
        }

        private bool IsPointInsideExtents(Point3d pt, Extents3d ext)
        {
            return pt.X >= ext.MinPoint.X && pt.X <= ext.MaxPoint.X && pt.Y >= ext.MinPoint.Y && pt.Y <= ext.MaxPoint.Y;
        }

        private Point3d GetEntityCenter(Entity ent)
        {
            try
            {
                if (ent.Bounds.HasValue)
                {
                    Extents3d ext = ent.Bounds.Value;
                    return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, 0);
                }
            }
            catch { }
            return Point3d.Origin;
        }

        private string GetAttributeValue(Transaction tr, BlockReference blk, string tag)
        {
            if (blk.AttributeCollection != null)
            {
                foreach (ObjectId attId in blk.AttributeCollection)
                {
                    AttributeReference att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null && att.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)) return att.TextString;
                }
            }
            return "";
        }
    }
}