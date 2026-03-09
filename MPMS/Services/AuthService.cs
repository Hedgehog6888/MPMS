using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private const int MaxRecentAccounts = 5;

    private AuthResponse? _current;

    public AuthService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public bool IsAuthenticated => _current is not null && _current.ExpiresAt > DateTime.UtcNow;
    public string? Token    => _current?.Token;
    public Guid?   UserId   => _current?.UserId;
    public string? UserName  => _current?.Name;
    public string? Username  => _current?.Username;
    public string? UserRole  => _current?.Role;

    public void SetSession(AuthResponse response)
    {
        _current = response;
        _ = PersistSessionAsync(response);
        _ = SaveRecentAccountAsync(response);
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

        if (session is null) return false;

        // Allow restoring an expired session when offline:
        // the user can continue working with cached local data.
        // On next successful API call the session will be refreshed.
        _current = new AuthResponse(
            session.UserId, session.UserName, session.Username,
            session.UserRole, session.Token, session.ExpiresAt);

        return true;
    }

    public async Task<List<RecentAccount>> GetRecentAccountsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.RecentAccounts
            .OrderByDescending(a => a.LastLoginAt)
            .Take(MaxRecentAccounts)
            .ToListAsync();
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private async Task PersistSessionAsync(AuthResponse r)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.AuthSessions.FirstOrDefaultAsync();

        if (existing is null)
        {
            db.AuthSessions.Add(new AuthSession
            {
                Id = 1, Token = r.Token, UserId = r.UserId,
                UserName = r.Name, Username = r.Username,
                UserRole = r.Role, ExpiresAt = r.ExpiresAt
            });
        }
        else
        {
            existing.Token = r.Token;
            existing.UserId = r.UserId;
            existing.UserName = r.Name;
            existing.Username = r.Username;
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

    private async Task SaveRecentAccountAsync(AuthResponse r)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.RecentAccounts
            .FirstOrDefaultAsync(a => a.Username == r.Username);

        if (existing is not null)
        {
            existing.DisplayName = r.Name;
            existing.Role = r.Role;
            existing.LastLoginAt = DateTime.UtcNow;
            // Refresh initials/color in case role changed
            var refreshed = RecentAccount.From(r.Username, r.Name, r.Role);
            existing.Initials = refreshed.Initials;
            existing.AvatarColor = refreshed.AvatarColor;
        }
        else
        {
            // Trim to MaxRecentAccounts - 1 before adding new
            var all = await db.RecentAccounts
                .OrderBy(a => a.LastLoginAt)
                .ToListAsync();

            while (all.Count >= MaxRecentAccounts)
            {
                db.RecentAccounts.Remove(all[0]);
                all.RemoveAt(0);
            }

            db.RecentAccounts.Add(RecentAccount.From(r.Username, r.Name, r.Role));
        }

        await db.SaveChangesAsync();
    }
}
