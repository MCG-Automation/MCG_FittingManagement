using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MCGCadPlugin.Models.FittingManagement;
using MCGCadPlugin.Utilities.FittingManagement;

namespace MCGCadPlugin.Services.FittingManagement
{
    /// <summary>
    /// Phần partial xử lý Project Library — load/create/save/auto-assign cho file project JSON do user chọn.
    /// </summary>
    public partial class FittingManagementService : IProjectLibraryService
    {
        public List<ProjectCatalogItem> LoadProjectCatalog(string projectJsonPath)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu LoadProjectCatalog: {projectJsonPath}");
            try
            {
                if (string.IsNullOrEmpty(projectJsonPath)) return new List<ProjectCatalogItem>();
                var items = CatalogJsonStore.Read<ProjectCatalogItem>(projectJsonPath);
                Debug.WriteLine($"{LOG_PREFIX} LoadProjectCatalog THÀNH CÔNG ({items.Count} items).");
                return items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI LoadProjectCatalog: {ex.Message}");
                throw;
            }
        }

        public void CreateProjectCatalog(string projectJsonPath)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu CreateProjectCatalog: {projectJsonPath}");
            try
            {
                if (string.IsNullOrEmpty(projectJsonPath)) throw new ArgumentException("Đường dẫn project JSON rỗng.");
                CatalogJsonStore.Write(projectJsonPath, new List<ProjectCatalogItem>());
                Debug.WriteLine($"{LOG_PREFIX} CreateProjectCatalog THÀNH CÔNG.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI CreateProjectCatalog: {ex.Message}");
                throw;
            }
        }

        public Tuple<int, int> MergeIntoProject(string projectJsonPath, List<CatalogItem> items)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu MergeIntoProject ({items?.Count ?? 0} items)...");
            try
            {
                if (string.IsNullOrEmpty(projectJsonPath)) throw new ArgumentException("Đường dẫn project JSON rỗng.");
                var result = CatalogJsonStore.MergeItems(projectJsonPath, items);
                Debug.WriteLine($"{LOG_PREFIX} MergeIntoProject THÀNH CÔNG (Mới: {result.Item1}, Sửa: {result.Item2}).");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI MergeIntoProject: {ex.Message}");
                throw;
            }
        }

        public void SaveProjectCatalog(string projectJsonPath, List<ProjectCatalogItem> catalog)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu SaveProjectCatalog ({catalog?.Count ?? 0} items)...");
            try
            {
                if (string.IsNullOrEmpty(projectJsonPath)) throw new ArgumentException("Đường dẫn project JSON rỗng.");
                CatalogJsonStore.Write(projectJsonPath, catalog ?? new List<ProjectCatalogItem>());
                Debug.WriteLine($"{LOG_PREFIX} SaveProjectCatalog THÀNH CÔNG.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI SaveProjectCatalog: {ex.Message}");
                throw;
            }
        }

        public int RemoveFromProject(string projectJsonPath, IEnumerable<string> blockNames)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu RemoveFromProject...");
            try
            {
                if (string.IsNullOrEmpty(projectJsonPath)) throw new ArgumentException("Đường dẫn project JSON rỗng.");
                int removed = CatalogJsonStore.RemoveItems(projectJsonPath, blockNames);
                Debug.WriteLine($"{LOG_PREFIX} RemoveFromProject THÀNH CÔNG ({removed} items).");
                return removed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI RemoveFromProject: {ex.Message}");
                throw;
            }
        }

        public int AutoAssignPositions(string projectJsonPath)
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu AutoAssignPositions: {projectJsonPath}");
            try
            {
                if (string.IsNullOrEmpty(projectJsonPath)) throw new ArgumentException("Đường dẫn project JSON rỗng.");
                var catalog = CatalogJsonStore.Read<ProjectCatalogItem>(projectJsonPath);

                var detailFittings = catalog
                    .Where(x => !string.IsNullOrWhiteSpace(x.BomType)
                                && (x.BomType.Equals("DETAIL", StringComparison.OrdinalIgnoreCase)
                                    || x.BomType.Equals("HULL", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (detailFittings.Count == 0)
                {
                    Debug.WriteLine($"{LOG_PREFIX} AutoAssignPositions: không có fitting Detail/Hull.");
                    return 0;
                }

                var groupedByPartId = detailFittings
                    .Where(x => !string.IsNullOrEmpty(x.PartNumber))
                    .GroupBy(x => x.PartNumber)
                    .OrderBy(g => g.Key)
                    .ToList();

                int posCounter = 1;
                foreach (var group in groupedByPartId)
                {
                    string posString = posCounter.ToString("D3");
                    foreach (var item in group) item.ProjectPosNum = posString;
                    posCounter++;
                }

                CatalogJsonStore.Write(projectJsonPath, catalog);
                Debug.WriteLine($"{LOG_PREFIX} AutoAssignPositions THÀNH CÔNG ({groupedByPartId.Count} groups).");
                return groupedByPartId.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI AutoAssignPositions: {ex.Message}");
                throw;
            }
        }
    }
}
