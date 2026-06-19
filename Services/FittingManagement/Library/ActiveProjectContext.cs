using System;
using System.Diagnostics;
using System.IO;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Singleton lưu Project Library đang active trong session AutoCAD.
    /// Master window đọc context để biết "Add to Active Project" có khả dụng không.
    /// Không persist xuống disk — reset khi AutoCAD tắt.
    /// </summary>
    public class ActiveProjectContext
    {
        private const string LOG_PREFIX = "[ActiveProjectContext]";
        private static readonly Lazy<ActiveProjectContext> _instance = new Lazy<ActiveProjectContext>(() => new ActiveProjectContext());
        public static ActiveProjectContext Instance => _instance.Value;

        private string _projectFilePath;

        /// <summary>
        /// Đường dẫn file project JSON đang active. Null hoặc rỗng = chưa có project nào.
        /// </summary>
        public string ProjectFilePath
        {
            get => _projectFilePath;
            set
            {
                if (_projectFilePath == value) return;
                _projectFilePath = value;
                Debug.WriteLine($"{LOG_PREFIX} Active project changed -> {_projectFilePath ?? "<none>"}");
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Tên hiển thị (filename không extension) của project đang active.</summary>
        public string ProjectDisplayName =>
            string.IsNullOrEmpty(_projectFilePath) ? string.Empty : Path.GetFileNameWithoutExtension(_projectFilePath);

        /// <summary>True nếu đã có project đang active và file vẫn tồn tại trên disk.</summary>
        public bool HasActiveProject =>
            !string.IsNullOrEmpty(_projectFilePath) && File.Exists(_projectFilePath);

        /// <summary>Bắn khi <see cref="ProjectFilePath"/> đổi giá trị.</summary>
        public event EventHandler ProjectChanged;

        private ActiveProjectContext()
        {
            Debug.WriteLine($"{LOG_PREFIX} Khởi tạo singleton.");
        }

        /// <summary>Xoá project active (set null).</summary>
        public void Clear()
        {
            ProjectFilePath = null;
        }
    }
}
