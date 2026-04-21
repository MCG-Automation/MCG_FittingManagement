using System;
using System.Diagnostics;
using System.Drawing;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Exception = System.Exception;
using MCGCadPlugin.Views.FittingManagement;

namespace MCGCadPlugin.Commands
{
    /// <summary>
    /// Quản lý PaletteSet duy nhất của toàn plugin — Singleton.
    /// Đây là nguồn gốc duy nhất (Single Source of Truth) cho PaletteSet.
    /// Tất cả Command của các Module đều gọi qua class này.
    /// KHÔNG tạo PaletteSet ở bất kỳ file nào khác.
    /// </summary>
    public sealed class PaletteManager
    {
        #region Singleton

        private const string LOG_PREFIX = "[PaletteManager]";

        // Instance duy nhất — thread-safe với Lazy
        private static readonly Lazy<PaletteManager> _instance =
            new Lazy<PaletteManager>(() => new PaletteManager());

        /// <summary>Truy cập instance duy nhất của PaletteManager</summary>
        public static PaletteManager Instance => _instance.Value;

        // Constructor private — ngăn tạo instance từ bên ngoài
        private PaletteManager() { }

        #endregion

        #region Fields

        private PaletteSet _paletteSet;

        /// <summary>
        /// GUID cố định — AutoCAD dùng để nhớ vị trí dock của palette.
        /// KHÔNG BAO GIỜ thay đổi giá trị này sau khi đã deploy.
        /// </summary>
        private static readonly Guid PaletteGuid =
            new Guid("2b80cfe9-c560-49d6-8a09-9d636260fcf2");
        #endregion

        #region Public Properties

        /// <summary>Kiểm tra PaletteSet đã được khởi tạo chưa</summary>
        public bool IsInitialized => _paletteSet != null;

        /// <summary>Kiểm tra PaletteSet đang hiển thị không</summary>
        public bool IsVisible => _paletteSet?.Visible ?? false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Hiển thị PaletteSet. Tự động khởi tạo nếu chưa có.
        /// Đây là method duy nhất các Command cần gọi để mở UI.
        /// </summary>
        public void Show()
        {
            Debug.WriteLine($"{LOG_PREFIX} Yêu cầu hiển thị PaletteSet...");
            try
            {
                if (!IsInitialized)
                    Initialize();

                _paletteSet.Visible = true;
                Debug.WriteLine($"{LOG_PREFIX} PaletteSet hiển thị THÀNH CÔNG.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} LỖI khi hiển thị: {ex.Message}");
                throw;
            }
        }

        /// <summary>Ẩn PaletteSet nếu đang hiển thị</summary>
        public void Hide()
        {
            Debug.WriteLine($"{LOG_PREFIX} Yêu cầu ẩn PaletteSet...");
            if (IsInitialized)
            {
                _paletteSet.Visible = false;
                Debug.WriteLine($"{LOG_PREFIX} PaletteSet đã ẩn.");
            }
        }

        /// <summary>Bật/tắt hiển thị PaletteSet</summary>
        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        #endregion

        #region AutoCAD Commands

        /// <summary>Lệnh hiển thị Palette (gõ MCG_Show trong CAD)</summary>
        [CommandMethod("MCG_Show", CommandFlags.Modal)]
        public void McgShow() => Show();

        /// <summary>Lệnh ẩn Palette (gõ MCG_Hide trong CAD)</summary>
        [CommandMethod("MCG_Hide", CommandFlags.Modal)]
        public void McgHide() => Hide();

        #endregion

        #region Private Methods

        /// <summary>
        /// Khởi tạo PaletteSet và tab Fitting Management.
        /// Chỉ chạy 1 lần duy nhất trong vòng đời plugin.
        /// </summary>
        private void Initialize()
        {
            Debug.WriteLine($"{LOG_PREFIX} Bắt đầu khởi tạo PaletteSet...");

            // 1. Tạo PaletteSet với GUID cố định
            _paletteSet = new PaletteSet("MCG Plugins", PaletteGuid);

            // 2. Nạp nội dung — PHẢI thực hiện TRƯỚC khi set Dock/Size
            _paletteSet.AddVisual("Fitting Management", new FittingManagementView());

            // 3. Thiết lập thuộc tính — SAU AddVisual
            _paletteSet.DockEnabled = DockSides.Right | DockSides.Left;
            _paletteSet.Size = new Size(400, 600);
            _paletteSet.Style = PaletteSetStyles.ShowTabForSingle
                              | PaletteSetStyles.Snappable;
            _paletteSet.KeepFocus = true;

            Debug.WriteLine($"{LOG_PREFIX} PaletteSet khởi tạo THÀNH CÔNG — 1 tab đã đăng ký.");
        }

        #endregion
    }
}