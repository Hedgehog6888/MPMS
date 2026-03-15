using MPMS.Models;

namespace MPMS.Services;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? Token { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    string? Username { get; }
    string? UserRole { get; }

    /// <summary>Saves session after successful online login (awaitable — ensures DB persistence).</summary>
    Task SetSessionAsync(AuthResponse response, string plainPassword);

    /// <summary>Fire-and-forget variant kept for internal use.</summary>
    void SetSession(AuthResponse response, string plainPassword);

    void Logout();
    Task<bool> TryRestoreSessionAsync();
    /// <summary>Returns (response, null) on success; (null, blockMessage) if deleted/blocked; (null, null) if wrong password.</summary>
    Task<(AuthResponse? Response, string? BlockMessage)> TryOfflineLoginAsync(string username, string plainPassword);
    /// <summary>Returns (true, null) if login allowed; (false, message) if deleted or blocked.</summary>
    Task<(bool Allowed, string? BlockMessage)> CanUserLoginAsync(Guid userId);
    Task<bool> HasLocalCacheAsync(string username);
    Task<List<RecentAccount>> GetRecentAccountsAsync();
}
