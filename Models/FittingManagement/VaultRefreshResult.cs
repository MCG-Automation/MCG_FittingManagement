namespace MCGCadPlugin.Models.FittingManagement
{
    /// <summary>Trạng thái kết quả khi thử pull latest từ Vault (qua Inventor AddIn).</summary>
    public enum VaultRefreshStatus
    {
        /// <summary>Refresh thành công — file local đã là latest.</summary>
        Success,

        /// <summary>File đã là latest từ trước (không cần refresh).</summary>
        AlreadyLatest,

        /// <summary>Inventor chưa có Vault AddIn cài đặt hoặc AddIn chưa Active.</summary>
        SkippedNoAddIn,

        /// <summary>Vault AddIn có nhưng user chưa login — skip, dùng file local.</summary>
        SkippedNotLoggedIn,

        /// <summary>File không nằm trong Vault — skip, dùng file local như cũ.</summary>
        SkippedNotInVault,

        /// <summary>Có lỗi khi gọi Vault API — log chi tiết, proceed với file local.</summary>
        Failed
    }

    /// <summary>
    /// Kết quả của 1 lần TryPullLatestFromVault.
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
            => new VaultRefreshResult { Status = VaultRefreshStatus.Success, FilePath = path, MethodUsed = methodUsed, Message = $"Refreshed via {methodUsed}" };

        public static VaultRefreshResult AlreadyLatest(string path)
            => new VaultRefreshResult { Status = VaultRefreshStatus.AlreadyLatest, FilePath = path, Message = "File local đã là latest." };

        public static VaultRefreshResult SkippedNoAddIn(string reason)
            => new VaultRefreshResult { Status = VaultRefreshStatus.SkippedNoAddIn, Message = reason };

        public static VaultRefreshResult SkippedNotLoggedIn()
            => new VaultRefreshResult { Status = VaultRefreshStatus.SkippedNotLoggedIn, Message = "Vault AddIn chưa login — mở Inventor → Vault menu → Log In trước." };

        public static VaultRefreshResult SkippedNotInVault(string path)
            => new VaultRefreshResult { Status = VaultRefreshStatus.SkippedNotInVault, FilePath = path, Message = "File không có trong Vault hoặc không tracked." };

        public static VaultRefreshResult Failed(string path, string error)
            => new VaultRefreshResult { Status = VaultRefreshStatus.Failed, FilePath = path, Message = error };
    }
}
