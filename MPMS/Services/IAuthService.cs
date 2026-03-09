using Microsoft.EntityFrameworkCore;
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

    void SetSession(AuthResponse response);
    void Logout();
    Task<bool> TryRestoreSessionAsync();
    Task<List<RecentAccount>> GetRecentAccountsAsync();
}
