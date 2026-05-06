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
            return Build(catalog, null, allLabel);
        }

        /// <summary>
        /// Build cây Categories có chèn thêm node "Recently" (ngay sau "All Fittings") nếu
        /// <paramref name="recentBlockNames"/> không rỗng — items giữ thứ tự mới nhất → cũ nhất,
        /// bỏ qua key không còn tồn tại trong catalog.
        /// </summary>
        public static List<CategoryNode> Build(IList<CatalogItem> catalog, IList<string> recentBlockNames, string allLabel = "All Fittings")
        {
            var roots = new List<CategoryNode>
            {
                new CategoryNode { CategoryName = allLabel, CountLabel = $"({catalog.Count})", Items = catalog.ToList() }
            };

            // Chèn node "Recently" ngay sau "All Fittings"
            if (recentBlockNames != null && recentBlockNames.Count > 0)
            {
                var byBlockName = catalog
                    .Where(c => !string.IsNullOrWhiteSpace(c.BlockName))
                    .GroupBy(c => c.BlockName, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                var recentItems = new List<CatalogItem>();
                foreach (var name in recentBlockNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (byBlockName.TryGetValue(name, out var item)) recentItems.Add(item);
                }

                if (recentItems.Count > 0)
                {
                    roots.Add(new CategoryNode
                    {
                        CategoryName = "Recently",
                        CountLabel = $"({recentItems.Count})",
                        Items = recentItems
                    });
                }
            }

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
