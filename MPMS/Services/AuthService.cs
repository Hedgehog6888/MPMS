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

        // Не восстанавливать сессию удалённого или заблокированного пользователя
        var (allowed, _) = await CanUserLoginAsync(session.UserId);
        if (!allowed)
        {
            session.IsActiveSession = false;
            session.Token = string.Empty;
            await db.SaveChangesAsync();
            return false;
        }

        _current = new AuthResponse(
            session.UserId, session.UserName, session.Username,
            session.UserRole, session.Token, session.ExpiresAt);

        return true;
    }

    public async Task<(bool Allowed, string? BlockMessage)> CanUserLoginAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.DeletedUserIds.AnyAsync(x => x.Id == userId))
            return (false, "Пользователь удалён");
        var user = await db.Users.FindAsync(userId);
        if (user is null) return (true, null); // Нет в локальной БД — разрешаем
        if (user.IsBlocked)
        {
            var reason = !string.IsNullOrWhiteSpace(user.BlockedReason)
                ? $" Причина: {user.BlockedReason}"
                : "";
            return (false, $"Пользователь заблокирован.{reason}");
        }
        return (true, null);
    }

    /// <summary>
    /// Attempts to authenticate using the locally cached password hash.
    /// First checks the active AuthSession (previously logged-in users),
    /// then falls back to LocalUser.PasswordHash (admin-created users).
    /// </summary>
    public async Task<(AuthResponse? Response, string? BlockMessage)> TryOfflineLoginAsync(string username, string plainPassword)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Primary check: active session (user has previously logged in online)
        var session = await db.AuthSessions.FirstOrDefaultAsync();
        if (session is not null
            && string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(session.LocalPasswordHash)
            && BCrypt.Net.BCrypt.Verify(plainPassword, session.LocalPasswordHash))
        {
            var (allowed, blockMessage) = await CanUserLoginAsync(session.UserId);
            if (!allowed) return (null, blockMessage);
            return (new AuthResponse(
                session.UserId, session.UserName, session.Username,
                session.UserRole, session.Token, session.ExpiresAt), null);
        }

        // Fallback: admin-created user with password stored in LocalUser.PasswordHash
        var localUser = await db.Users
            .FirstOrDefaultAsync(u => u.Username == username);
        if (localUser is null
            || string.IsNullOrEmpty(localUser.PasswordHash)
            || !BCrypt.Net.BCrypt.Verify(plainPassword, localUser.PasswordHash))
            return (null, null);
        if (localUser.IsBlocked)
        {
            var reason = !string.IsNullOrWhiteSpace(localUser.BlockedReason)
                ? $" Причина: {localUser.BlockedReason}"
                : "";
            return (null, $"Пользователь заблокирован.{reason}");
        }

        return (new AuthResponse(
            localUser.Id, localUser.Name, localUser.Username,
            localUser.RoleName, string.Empty, DateTime.UtcNow.AddDays(1)), null);
    }

    public async Task<bool> HasLocalCacheAsync(string username)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var session = await db.AuthSessions.FirstOrDefaultAsync();
        if (session is not null
            && string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(session.LocalPasswordHash))
            return true;

        var localUser = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        return localUser is not null && !string.IsNullOrEmpty(localUser.PasswordHash);
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
