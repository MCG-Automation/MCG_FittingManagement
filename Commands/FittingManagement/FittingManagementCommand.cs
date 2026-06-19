using System.Diagnostics;
using Autodesk.AutoCAD.Runtime;
using MCG_FittingManagement.Commands; // Gọi đến PaletteManager của hệ thống

namespace MCG_FittingManagement.Commands.FittingManagement
{
    /// <summary>
    /// Đăng ký 1 lệnh duy nhất để gọi toàn bộ giao diện Fitting Tools.
    /// Toàn bộ thao tác nghiệp vụ sẽ được điều khiển qua các Button trên giao diện.
    /// </summary>
    public class FittingManagementCommand
    {
        private const string LOG_PREFIX = "[FittingCommand]";

        // Kỹ sư chỉ cần gõ 1 lệnh này để mở UI, hoặc dùng lệnh hệ thống MCG_Show
        [CommandMethod("MCG_Fitting", CommandFlags.Modal)]
        public void ShowFittingTools()
        {
            Debug.WriteLine($"{LOG_PREFIX} Kích hoạt lệnh MCG_Fitting...");
            
            // Gọi Singleton PaletteManager để bật toàn bộ giao diện (chứa 5 Tab) lên màn hình
            PaletteManager.Instance.Show();
        }
    }
}