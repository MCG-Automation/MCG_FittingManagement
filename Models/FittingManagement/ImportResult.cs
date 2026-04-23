using System.Collections.Generic;

namespace MCGCadPlugin.Models.FittingManagement
{
    /// <summary>
    /// Ket qua cua mot batch import (IDW hoac JSON).
    /// Chua so luong thanh cong, that bai va danh sach loi chi tiet tung file.
    /// </summary>
    public class ImportResult
    {
        /// <summary>So file import thanh cong.</summary>
        public int SuccessCount { get; set; }

        /// <summary>So file that bai hoac bi bo qua.</summary>
        public int FailCount { get; set; }

        /// <summary>Danh sach thong bao loi tung file (format: "filename: error message").</summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Ket qua pull latest tu Vault cho tung file (neu enabled).
        /// Empty neu pullFromVault=false hoac chua chay Vault step.
        /// </summary>
        public List<VaultRefreshResult> VaultResults { get; set; } = new List<VaultRefreshResult>();

        /// <summary>Them loi vao danh sach.</summary>
        public void AddError(string fileName, string errorMessage)
        {
            Errors.Add($"• {fileName}: {errorMessage}");
        }
    }
}
