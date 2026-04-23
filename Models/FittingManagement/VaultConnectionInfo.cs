namespace MCGCadPlugin.Models.FittingManagement
{
    /// <summary>
    /// Thông tin kết nối Vault (auto-detect từ LoginHistory.xml hoặc user config).
    /// Credentials KHÔNG lưu ở đây — VDF tự lấy từ Windows Credential Manager / SSO.
    /// </summary>
    public class VaultConnectionInfo
    {
        /// <summary>Tên server Vault (vd "VNHPH1-S0006").</summary>
        public string Server { get; set; }

        /// <summary>Tên vault/database trên server (vd "MacGregor_CAS").</summary>
        public string Vault { get; set; }

        /// <summary>Username cached từ LoginHistory (có thể rỗng nếu dùng SSO hoặc chưa tick Remember).</summary>
        public string UserName { get; set; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Server) && !string.IsNullOrWhiteSpace(Vault);

        public override string ToString()
            => IsValid ? $"{Server}/{Vault}" + (string.IsNullOrEmpty(UserName) ? "" : $" (user={UserName})") : "(invalid)";
    }
}
