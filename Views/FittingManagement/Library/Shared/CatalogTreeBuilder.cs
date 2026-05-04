using System;
using System.Collections.Generic;
using System.Linq;
using MCGCadPlugin.Models.FittingManagement;

namespace MCGCadPlugin.Views.FittingManagement
{
    /// <summary>
    /// Build TreeView nodes + filter logic dùng chung cho Master/Project Library window.
    /// Nhận <see cref="CatalogItem"/> base nên 2 window đều dùng được (ProjectCatalogItem kế thừa CatalogItem).
    /// </summary>
    public static class CatalogTreeBuilder
    {
        /// <summary>Build cây Categories: root "All" + group BomType → Title.</summary>
        public static List<CategoryNode> Build(IList<CatalogItem> catalog, string allLabel = "All Fittings")
        {
            var roots = new List<CategoryNode>
            {
                new CategoryNode { CategoryName = allLabel, CountLabel = $"({catalog.Count})", Items = catalog.ToList() }
            };

            var bomGroups = catalog.GroupBy(x =>
            {
                if (string.IsNullOrWhiteSpace(x.BomType)) return "Uncategorized (Legacy)";
                string type = x.BomType.ToUpperInvariant();
                if (type == "PANEL") return "Fitting In Panel";
                if (type == "DETAIL" || type == "HULL") return "Fitting In Detail";
                return "Uncategorized (Legacy)";
            }).OrderBy(g => g.Key);

            foreach (var bg in bomGroups)
            {
                var bomNode = new CategoryNode
                {
                    CategoryName = bg.Key,
                    CountLabel = $"({bg.Count()})",
                    Items = bg.ToList()
                };
                var catGroups = bg.GroupBy(x => string.IsNullOrWhiteSpace(x.Title) ? "Uncategorized" : x.Title.Trim())
                                  .OrderBy(g => g.Key);
                foreach (var cg in catGroups)
                {
                    bomNode.Children.Add(new CategoryNode
                    {
                        CategoryName = cg.Key,
                        CountLabel = $"({cg.Count()})",
                        Items = cg.ToList()
                    });
                }
                roots.Add(bomNode);
            }
            return roots;
        }

        /// <summary>Filter catalog theo chuỗi search (multi-keyword, case-insensitive, AND logic).</summary>
        public static List<CatalogItem> ApplySearch(IEnumerable<CatalogItem> source, string searchText)
        {
            if (source == null) return new List<CatalogItem>();
            if (string.IsNullOrWhiteSpace(searchText)) return source.ToList();

            string[] keywords = searchText.ToLowerInvariant()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            return source.Where(i => keywords.All(kw =>
                (i.PartNumber != null && i.PartNumber.ToLowerInvariant().Contains(kw)) ||
                (i.BlockName != null && i.BlockName.ToLowerInvariant().Contains(kw)) ||
                (i.Description != null && i.Description.ToLowerInvariant().Contains(kw)) ||
                (i.Title != null && i.Title.ToLowerInvariant().Contains(kw)) ||
                (i.Designer != null && i.Designer.ToLowerInvariant().Contains(kw)) ||
                (i.EntityType != null && i.EntityType.ToLowerInvariant().Contains(kw))
            )).ToList();
        }
    }
}
