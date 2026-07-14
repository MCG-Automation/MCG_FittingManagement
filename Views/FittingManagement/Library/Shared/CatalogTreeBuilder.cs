using System;
using System.Collections.Generic;
using System.Linq;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Views.FittingManagement
{
    /// <summary>
    /// Build TreeView nodes + filter logic dùng cho Fitting Table window (catalog của Project Folder
    /// đang active — xem <see cref="MCG_FittingManagement.Services.FittingManagement.ActiveProjectContext"/>).
    /// </summary>
    public static class CatalogTreeBuilder
    {
        /// <summary>Build cây Categories: root "All" + group BomType → PartNumber (1 Sub Folder = đúng
        /// 1 PartNumber, sort theo CategorySortOrder — thứ tự hiển thị do user tự kéo-thả, KHÔNG PHẢI
        /// ProjectPosNum thật của BOM/bản vẽ).</summary>
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
                new CategoryNode { CategoryName = allLabel, CountLabel = $"({catalog.Count})", Items = catalog.ToList(), Level = 0 }
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
                        Items = recentItems,
                        Level = 0
                    });
                }
            }

            var bomGroups = catalog.GroupBy(x =>
            {
                if (string.IsNullOrWhiteSpace(x.BomType)) return "Uncategorized (Legacy)";
                string type = x.BomType.ToUpperInvariant();
                if (type == "PANEL" || type == "EQUIPMENT") return "Fitting In Equipment";
                if (type == "DETAIL" || type == "HULL") return "Fitting In Hull";
                return "Uncategorized (Legacy)";
            }).OrderBy(g => g.Key);

            foreach (var bg in bomGroups)
            {
                var bomNode = new CategoryNode
                {
                    CategoryName = bg.Key,
                    CountLabel = $"({bg.Count()})",
                    Items = bg.ToList(),
                    Level = 1
                };

                // Sub Folder = ĐÚNG 1 PartNumber (không group theo Title nữa — 1 Title có thể chứa
                // nhiều PartNumber khác nhau, gây mập mờ khi kéo-thả sắp xếp thứ tự, xem yêu cầu user).
                // Sort theo CategorySortOrder (thứ tự HIỂN THỊ do user tự kéo-thả — KHÔNG PHẢI
                // ProjectPosNum, vì 1 PartNumber có thể có nhiều ProjectPosNum khác nhau ở Equipment).
                // Chưa từng sắp xếp (null) → xếp CUỐI, tie-break alphabet theo PartNumber.
                var catGroups = bg.Where(x => !string.IsNullOrWhiteSpace(x.PartNumber))
                                  .GroupBy(x => x.PartNumber, StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(g => g.First().CategorySortOrder ?? int.MaxValue)
                                  .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
                foreach (var cg in catGroups)
                {
                    string partNumber = cg.Key;
                    string title = cg.First().Title;
                    int? sortOrder = cg.First().CategorySortOrder;
                    string orderLabel = sortOrder.HasValue ? sortOrder.Value.ToString("D3") : "—";
                    string displayTitle = string.IsNullOrWhiteSpace(title) ? "Uncategorized" : title.Trim();

                    bomNode.Children.Add(new CategoryNode
                    {
                        CategoryName = $"{orderLabel}-{displayTitle} ({partNumber})",
                        CountLabel = $"({cg.Count()})",
                        Items = cg.ToList(),
                        Level = 2,
                        Parent = bomNode
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
                (i.BlockName != null && i.BlockName.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant().Contains(kw)) ||
                (i.Description != null && i.Description.ToLowerInvariant().Contains(kw)) ||
                (i.Title != null && i.Title.ToLowerInvariant().Contains(kw)) ||
                (i.Designer != null && i.Designer.ToLowerInvariant().Contains(kw)) ||
                (i.EntityType != null && i.EntityType.ToLowerInvariant().Contains(kw))
            )).ToList();
        }
    }
}
