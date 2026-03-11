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

    public bool IsAuthenticated => _current is not null;
    public string? Token    => _current?.Token;
    public Guid?   UserId   => _current?.UserId;
    public string? UserName  => _current?.Name;
    public string? Username  => _current?.Username;
    public string? UserRole  => _current?.Role;

    public async Task SetSessionAsync(AuthResponse response, string plainPassword)
    {
        _current = response;
        await PersistSessionAsync(response, plainPassword);
        _ = SaveRecentAccountAsync(response);
    }

    public void SetSession(AuthResponse response, string plainPassword)
    {
        _current = response;
        _ = PersistSessionAsync(response, plainPassword);
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

        // Only auto-restore if the user was actively logged in (didn't explicitly log out).
        if (session is null || !session.IsActiveSession) return false;

        // Allow restoring session offline — user works with cached local data.
        _current = new AuthResponse(
            session.UserId, session.UserName, session.Username,
            session.UserRole, session.Token, session.ExpiresAt);

        return true;
    }

    /// <summary>
    /// Attempts to authenticate using the locally cached password hash.
    /// Only works if the user has previously logged in on this machine.
    /// </summary>
    public async Task<AuthResponse?> TryOfflineLoginAsync(string username, string plainPassword)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions.FirstOrDefaultAsync();

        if (session is null
            || !string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(session.LocalPasswordHash))
            return null;

        if (!BCrypt.Net.BCrypt.Verify(plainPassword, session.LocalPasswordHash))
            return null;

        return new AuthResponse(
            session.UserId, session.UserName, session.Username,
            session.UserRole, session.Token, session.ExpiresAt);
    }

    public async Task<bool> HasLocalCacheAsync(string username)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions.FirstOrDefaultAsync();
        return session is not null
               && string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrEmpty(session.LocalPasswordHash);
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
    private async Task PersistSessionAsync(AuthResponse r, string plainPassword)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.AuthSessions.FirstOrDefaultAsync();
        var pwdHash  = BCrypt.Net.BCrypt.HashPassword(plainPassword);

        if (existing is null)
        {
            db.AuthSessions.Add(new AuthSession
            {
                Id = 1, Token = r.Token, UserId = r.UserId,
                UserName = r.Name, Username = r.Username,
                UserRole = r.Role, ExpiresAt = r.ExpiresAt,
                LocalPasswordHash = pwdHash,
                IsActiveSession = true
            });
        }
        else
        {
            existing.Token             = r.Token;
            existing.UserId            = r.UserId;
            existing.UserName          = r.Name;
            existing.Username          = r.Username;
            existing.UserRole          = r.Role;
            existing.ExpiresAt         = r.ExpiresAt;
            existing.LocalPasswordHash = pwdHash;
            existing.IsActiveSession   = true;
        }

        await db.SaveChangesAsync();
    }

    private async Task ClearSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions.FirstOrDefaultAsync();
        if (session is not null)
        {
            // Mark as logged-out but keep the record so the same account
            // can log back in offline using the cached password hash.
            session.IsActiveSession = false;
            session.Token = string.Empty;
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
            existing.Role        = r.Role;
            existing.LastLoginAt = DateTime.UtcNow;
            var refreshed = RecentAccount.From(r.Username, r.Name, r.Role);
            existing.Initials    = refreshed.Initials;
            existing.AvatarColor = refreshed.AvatarColor;
        }
        else
        {
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
