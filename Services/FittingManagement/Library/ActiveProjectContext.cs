using System;
using System.Diagnostics;
using System.IO;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Singleton lưu Project Folder đang active trong session AutoCAD — mô hình "1 Project = 1 Folder"
    /// (mọi catalog JSON, block .dwg, recent-items, diagnostic report đều nằm trong folder này, xem
    /// <see cref="FittingManagementService.MasterLibraryFolder"/>). Persist Folder gần nhất qua
    /// <see cref="LastProjectSettingsStore"/> (%APPDATA%) — tự restore lại khi mở AutoCAD lần sau,
    /// không bắt user Load Folder lại mỗi phiên.
    /// </summary>
    public class ActiveProjectContext
    {
        private const string LOG_PREFIX = "[ActiveProjectContext]";
        private const string CATALOG_FILE_NAME = "FittingCatalog.json";
        private static readonly Lazy<ActiveProjectContext> _instance = new Lazy<ActiveProjectContext>(() => new ActiveProjectContext());
        public static ActiveProjectContext Instance => _instance.Value;

        private string _projectFolderPath;

        /// <summary>
        /// Đường dẫn folder project đang active. Null/rỗng = chưa có project nào.
        /// </summary>
        public string ProjectFolderPath
        {
            get => _projectFolderPath;
            set
            {
                if (_projectFolderPath == value) return;
                _projectFolderPath = value;
                Debug.WriteLine($"{LOG_PREFIX} Active project folder changed -> {_projectFolderPath ?? "<none>"}");
                LastProjectSettingsStore.SaveLastFolder(_projectFolderPath);
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Tên hiển thị (tên folder) của project đang active.</summary>
        public string ProjectDisplayName =>
            string.IsNullOrEmpty(_projectFolderPath) ? string.Empty : new DirectoryInfo(_projectFolderPath).Name;

        /// <summary>True nếu đã có project đang active và folder vẫn tồn tại trên disk.</summary>
        public bool HasActiveProject =>
            !string.IsNullOrEmpty(_projectFolderPath) && Directory.Exists(_projectFolderPath);

        /// <summary>
        /// Đường dẫn cố định tới file catalog (<c>FittingCatalog.json</c>) bên trong Project Folder đang
        /// active — nguồn chân lý DUY NHẤT cho tên file này, dùng lại ở mọi nơi thay vì lặp chuỗi.
        /// </summary>
        public string CatalogFilePath =>
            HasActiveProject ? Path.Combine(_projectFolderPath, CATALOG_FILE_NAME) : null;

        /// <summary>Bắn khi <see cref="ProjectFolderPath"/> đổi giá trị.</summary>
        public event EventHandler ProjectChanged;

        private ActiveProjectContext()
        {
            string restored = LastProjectSettingsStore.LoadLastFolder();
            if (!string.IsNullOrEmpty(restored) && Directory.Exists(restored))
            {
                _projectFolderPath = restored;
                Debug.WriteLine($"{LOG_PREFIX} Khởi tạo singleton — restore project folder gần nhất: {restored}");
            }
            else
            {
                Debug.WriteLine($"{LOG_PREFIX} Khởi tạo singleton — chưa có project folder nào (cần Load Folder).");
            }
        }

        /// <summary>Xoá project active (set null).</summary>
        public void Clear()
        {
            ProjectFolderPath = null;
        }
    }
}
