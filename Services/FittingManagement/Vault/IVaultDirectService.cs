using System;
using MCG_FittingManagement.Models.FittingManagement;

namespace MCG_FittingManagement.Services.FittingManagement
{
    /// <summary>
    /// Vault SDK direct access — bypass Inventor, gọi thẳng VDF API để pull latest file về local.
    /// Workflow: AutoDetectConnection → EnsureSignedIn (silent nếu có cached creds) → DownloadLatest per file.
    /// </summary>
    public interface IVaultDirectService : IDisposable
    {
        /// <summary>
        /// Đọc LoginHistory.xml của Vault Client → trả về Server + Vault gần nhất user dùng.
        /// Return null nếu file không tồn tại / malformed / không có LastServer.
        /// </summary>
        VaultConnectionInfo AutoDetectConnection();

        /// <summary>
        /// Đảm bảo đã login Vault server. Silent nếu Windows Credential Manager có cached token;
        /// nếu không có → bật dialog VDF giống Vault Explorer (user gõ password 1 lần).
        /// Gọi 1 lần trước batch download. Idempotent — re-call khi đã signed-in trả true ngay.
        /// </summary>
        /// <param name="conn">Connection info (từ AutoDetectConnection hoặc config tay).</param>
        /// <returns>true = signed in OK; false = user cancel dialog / login fail.</returns>
        bool EnsureSignedIn(VaultConnectionInfo conn);

        /// <summary>
        /// Download latest version của file (search theo tên file, không path) về <paramref name="localPath"/>.
        /// Overwrite file local nếu có. Gọi SAU <see cref="EnsureSignedIn"/> đã return true.
        /// </summary>
        /// <param name="fileName">Tên file không path (vd "CAS-0057372.idw").</param>
        /// <param name="localPath">Đường dẫn local để ghi file (vd "D:\\temp\\CAS-0057372.idw").</param>
        /// <returns>VaultRefreshResult — Success/SkippedNotInVault/Failed tùy kết quả.</returns>
        VaultRefreshResult DownloadLatest(string fileName, string localPath);

        /// <summary>True nếu đang trong session đã signed-in (khỏi check lại Mỗi file).</summary>
        bool IsSignedIn { get; }
    }
}
