using MPMS.Models;

namespace MPMS.Services;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? Token { get; }
    string? RefreshToken { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    string? Username { get; }
    string? UserRole { get; }

    string ApiBaseUrl { get; }

    Task PersistApiBaseUrlForNextLoginAsync(string urlInput);

    Task SetSessionAsync(AuthResponse response, string plainPassword);

    void SetSession(AuthResponse response, string plainPassword);

    Task<bool> TryRefreshJwtIfNeededAsync(IApiService api);

    void Logout();

    Task UpdateCurrentUserAsync(string newName, string newUsername);

    Task<bool> TryRestoreSessionAsync();
    Task<(AuthResponse? Response, string? BlockMessage)> TryOfflineLoginAsync(string username, string plainPassword);
    Task<(bool Allowed, string? BlockMessage)> CanUserLoginAsync(Guid userId);
    Task<bool> HasLocalCacheAsync(string username);
    Task<List<RecentAccount>> GetRecentAccountsAsync();
}
