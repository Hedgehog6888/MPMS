using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

public class AuthService : IAuthService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private const int MaxRecentAccounts = 5;

    private const string DefaultApiBase = "http://localhost:5147/api/";

    private readonly string _defaultApiBaseUrl;
    private string? _activeApiBaseUrl;

    private AuthResponse? _current;
    /// <summary>Пароль текущей сессии (только в памяти) — нужен для получения JWT после офлайн-входа при появлении сети.</summary>
    private string? _sessionPlainPassword;

    public AuthService(IDbContextFactory<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
        _defaultApiBaseUrl = ReadDefaultApiBaseUrlFromAppSettings();
    }

    public bool IsAuthenticated => _current is not null;
    public string? Token    => _current?.Token;
    public Guid?   UserId   => _current?.UserId;
    public string? UserName  => _current?.Name;
    public string? Username  => _current?.Username;
    public string? UserRole  => _current?.Role;

    /// <inheritdoc />
    public string ApiBaseUrl => NormalizeApiBaseUrl(_activeApiBaseUrl ?? _defaultApiBaseUrl);

    public async Task SetSessionAsync(AuthResponse response, string plainPassword)
    {
        _current = response;
        _sessionPlainPassword = plainPassword;
        _activeApiBaseUrl = null;
        await PersistSessionAsync(response, plainPassword);
        _ = SaveRecentAccountAsync(response);
    }

    public void SetSession(AuthResponse response, string plainPassword)
    {
        _current = response;
        _sessionPlainPassword = plainPassword;
        _activeApiBaseUrl = null;
        _ = PersistSessionAsync(response, plainPassword);
        _ = SaveRecentAccountAsync(response);
    }

    public async Task<bool> TryRefreshJwtIfNeededAsync(IApiService api)
    {
        var hasPassword = !string.IsNullOrEmpty(_sessionPlainPassword) && !string.IsNullOrWhiteSpace(Username);

        if (!string.IsNullOrWhiteSpace(Token))
        {
            if (await api.VerifyAuthAsync())
                return true;
        }

        if (!hasPassword)
            return false;

        var result = await api.LoginAsync(Username!.Trim(), _sessionPlainPassword!);
        if (!result.Success || result.Response is null) return false;

        var (allowed, _) = await CanUserLoginAsync(result.Response.UserId);
        if (!allowed) return false;

        _current = result.Response;
        await PersistSessionAsync(result.Response, _sessionPlainPassword!);
        return true;
    }

    public void Logout()
    {
        var currentUserId = _current?.UserId;
        _current = null;
        _sessionPlainPassword = null;
        _activeApiBaseUrl = null;
        _ = ClearSessionAsync(currentUserId);
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions
            .Where(s => s.IsActiveSession)
            .OrderByDescending(s => s.ExpiresAt)
            .FirstOrDefaultAsync();

        if (session is null || !session.IsActiveSession) return false;

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

        ApplyRestoredApiUrl(session);
        _sessionPlainPassword = TryDecryptSessionPassword(session.SessionPasswordProtected);

        return true;
    }

    public async Task<(bool Allowed, string? BlockMessage)> CanUserLoginAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        if (await db.DeletedUserIds.AnyAsync(x => x.Id == userId))
            return (false, "Пользователь удалён");
        var user = await db.Users.FindAsync(userId);
        if (user is null) return (true, null);
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

        var session = (await db.AuthSessions
                .OrderByDescending(s => s.ExpiresAt)
                .ToListAsync())
            .FirstOrDefault(s => string.Equals(s.Username, username, StringComparison.OrdinalIgnoreCase));
        if (session is not null
            && !string.IsNullOrEmpty(session.LocalPasswordHash)
            && BCrypt.Net.BCrypt.Verify(plainPassword, session.LocalPasswordHash))
        {
            var (allowed, blockMessage) = await CanUserLoginAsync(session.UserId);
            if (!allowed) return (null, blockMessage);
            return (new AuthResponse(
                session.UserId, session.UserName, session.Username,
                session.UserRole, session.Token, session.ExpiresAt), null);
        }

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

        var session = (await db.AuthSessions
                .OrderByDescending(s => s.ExpiresAt)
                .ToListAsync())
            .FirstOrDefault(s => string.Equals(s.Username, username, StringComparison.OrdinalIgnoreCase));
        if (session is not null && !string.IsNullOrEmpty(session.LocalPasswordHash))
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

    private void ApplyRestoredApiUrl(AuthSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.ApiBaseUrl))
            _activeApiBaseUrl = session.ApiBaseUrl;
        else
            _activeApiBaseUrl = null;
    }

    private static string ReadDefaultApiBaseUrlFromAppSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return DefaultApiBase;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("ApiBaseUrl", out var el))
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return NormalizeApiBaseUrl(s);
            }
        }
        catch { /* ignore */ }
        return DefaultApiBase;
    }

    private static string NormalizeApiBaseUrl(string input)
    {
        var s = input.Trim();
        if (string.IsNullOrEmpty(s)) return DefaultApiBase;
        s = s.TrimEnd('/');
        if (!s.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            s += "/api";
        return s + "/";
    }

    private static string? ProtectPlainPassword(string plainPassword)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainPassword);
#pragma warning disable CA1416 // WPF: DPAPI только Windows
            return Convert.ToBase64String(
                ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
#pragma warning restore CA1416
        }
        catch
        {
            return null;
        }
    }

    private static string? TryDecryptSessionPassword(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            var raw = Convert.FromBase64String(stored);
#pragma warning disable CA1416
            var bytes = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    private async Task PersistSessionAsync(AuthResponse r, string plainPassword)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = (await db.AuthSessions
                .OrderByDescending(s => s.ExpiresAt)
                .ToListAsync())
            .FirstOrDefault(s => string.Equals(s.Username, r.Username, StringComparison.OrdinalIgnoreCase));
        var pwdHash  = BCrypt.Net.BCrypt.HashPassword(plainPassword);
        var apiUrl   = NormalizeApiBaseUrl(_activeApiBaseUrl ?? _defaultApiBaseUrl);
        var encPlain = ProtectPlainPassword(plainPassword);
        var allSessions = await db.AuthSessions.ToListAsync();
        foreach (var session in allSessions)
            session.IsActiveSession = false;

        if (existing is null)
        {
            db.AuthSessions.Add(new AuthSession
            {
                Token = r.Token, UserId = r.UserId,
                UserName = r.Name, Username = r.Username,
                UserRole = r.Role, ExpiresAt = r.ExpiresAt,
                LocalPasswordHash = pwdHash,
                ApiBaseUrl = apiUrl,
                SessionPasswordProtected = encPlain,
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
            existing.ApiBaseUrl        = apiUrl;
            existing.SessionPasswordProtected = encPlain;
            existing.IsActiveSession   = true;
        }

        await db.SaveChangesAsync();
    }

    private async Task ClearSessionAsync(Guid? userId)
    {
        if (userId is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.AuthSessions
            .Where(s => s.UserId == userId.Value)
            .OrderByDescending(s => s.ExpiresAt)
            .FirstOrDefaultAsync();
        if (session is not null && session.IsActiveSession)
        {
            session.IsActiveSession = false;
            session.Token = string.Empty;
            session.SessionPasswordProtected = null;
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
