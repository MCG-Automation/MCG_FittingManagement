namespace MCG_FittingManagement.Models.FittingManagement
{
    /// <summary>Trạng thái kết quả khi thử pull latest từ Vault qua VDF SDK.</summary>
    public enum VaultRefreshStatus
    {
        /// <summary>Download thành công — file local đã là latest version.</summary>
        Success,

        /// <summary>File đã là latest từ trước (không cần download lại).</summary>
        AlreadyLatest,

        /// <summary>Vault Client/SDK không cài trên máy — bỏ qua refresh.</summary>
        SkippedNoAddIn,

        /// <summary>User chưa sign-in Vault (cancel dialog hoặc credentials fail) — skip.</summary>
        SkippedNotLoggedIn,

        /// <summary>File không có trong Vault — skip, dùng file local như cũ.</summary>
        SkippedNotInVault,

        /// <summary>Có lỗi khi gọi Vault API — log chi tiết, proceed với file local.</summary>
        Failed
    }

    /// <summary>
    /// Kết quả của 1 lần thử refresh/download file từ Vault.
    /// Không throw — caller đọc Status để quyết định proceed hay không.
    /// </summary>
    public class VaultRefreshResult
    {
        public VaultRefreshStatus Status { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
        public string MethodUsed { get; set; } // tên Vault API method đã hoạt động (cho debug/log)

        public bool IsSuccess => Status == VaultRefreshStatus.Success || Status == VaultRefreshStatus.AlreadyLatest;

        public static VaultRefreshResult Success(string path, string methodUsed)
            => new VaultRefreshResult { Status = VaultRefreshStatus.Success, FilePath = path, MethodUsed = methodUsed, Message = $"Updated via {methodUsed}" };

        public static VaultRefreshResult AlreadyLatest(string path)
            => new VaultRefreshResult { Status = VaultRefreshStatus.AlreadyLatest, FilePath = path, Message = "Local file is already the latest version." };

        public static VaultRefreshResult SkippedNoAddIn(string reason)
            => new VaultRefreshResult { Status = VaultRefreshStatus.SkippedNoAddIn, Message = reason };

        public static VaultRefreshResult SkippedNotLoggedIn()
            => new VaultRefreshResult { Status = VaultRefreshStatus.SkippedNotLoggedIn, Message = "Not signed in to Vault — sign-in dialog was cancelled or credentials are invalid." };

        public static VaultRefreshResult SkippedNotInVault(string path)
            => new VaultRefreshResult { Status = VaultRefreshStatus.SkippedNotInVault, FilePath = path, Message = "File not found in Vault or not tracked." };

        public static VaultRefreshResult Failed(string path, string error)
            => new VaultRefreshResult { Status = VaultRefreshStatus.Failed, FilePath = path, Message = error };
    }
}
