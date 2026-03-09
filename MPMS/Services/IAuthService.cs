using MPMS.Models;

namespace MPMS.Services;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? Token { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    string? UserEmail { get; }
    string? UserRole { get; }

    void SetSession(AuthResponse response);
    void Logout();
    Task<bool> TryRestoreSessionAsync();
}
