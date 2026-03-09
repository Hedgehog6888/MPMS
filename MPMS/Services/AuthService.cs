using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    private AuthResponse? _current;

    public AuthService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public bool IsAuthenticated => _current is not null && _current.ExpiresAt > DateTime.UtcNow;
    public string? Token    => _current?.Token;
    public Guid?   UserId   => _current?.UserId;
    public string? UserName  => _current?.Name;
    public string? UserEmail => _current?.Email;
    public string? UserRole  => _current?.Role;

    public void SetSession(AuthResponse response)
    {
        _current = response;
        _ = PersistSessionAsync(response);
    }

    public void Logout()
    {
        _current = null;
        _ = ClearSessionAsync();
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions.FirstOrDefaultAsync();

        if (session is null || session.ExpiresAt <= DateTime.UtcNow)
            return false;

        _current = new AuthResponse(
            session.UserId, session.UserName, session.UserEmail,
            session.UserRole, session.Token, session.ExpiresAt);

        return true;
    }

    private async Task PersistSessionAsync(AuthResponse r)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.AuthSessions.FirstOrDefaultAsync();

        if (existing is null)
        {
            db.AuthSessions.Add(new AuthSession
            {
                Id = 1, Token = r.Token, UserId = r.UserId,
                UserName = r.Name, UserEmail = r.Email,
                UserRole = r.Role, ExpiresAt = r.ExpiresAt
            });
        }
        else
        {
            existing.Token = r.Token;
            existing.UserId = r.UserId;
            existing.UserName = r.Name;
            existing.UserEmail = r.Email;
            existing.UserRole = r.Role;
            existing.ExpiresAt = r.ExpiresAt;
        }

        await db.SaveChangesAsync();
    }

    private async Task ClearSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions.FirstOrDefaultAsync();
        if (session is not null)
        {
            db.AuthSessions.Remove(session);
            await db.SaveChangesAsync();
        }
    }
}
