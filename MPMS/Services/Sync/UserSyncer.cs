using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using System.Text.Json;

namespace MPMS.Services.Sync;

public class UserSyncer : IEntitySyncer
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly JsonSerializerOptions _jsonOptions;

    public UserSyncer(IApiService api, IAuthService auth, JsonSerializerOptions jsonOptions)
    {
        _api = api;
        _auth = auth;
        _jsonOptions = jsonOptions;
    }

    public bool CanHandle(string entityType) => 
        entityType is "User" or "UserProfile";

    public Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    public async Task PullAsync(LocalDbContext db)
    {
        // Roles
        var apiRoles = await _api.GetRolesAsync();
        if (apiRoles is not null)
        {
            foreach (var r in apiRoles)
            {
                var existing = await db.Roles.FindAsync(r.Id);
                if (existing is null)
                    db.Roles.Add(new LocalRole { Id = r.Id, Name = r.Name, Description = r.Description });
                else
                {
                    existing.Name = r.Name;
                    existing.Description = r.Description;
                }
            }
        }

        // Users
        var users = await _api.GetUsersAsync();
        if (users is not null)
            await UserListMergeHelper.ApplyPulledUsersAsync(db, users, _auth);
    }

    public async Task<bool> PushAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.EntityType == "User") return await SyncUserAvatarAsync(db, op);
        if (op.EntityType == "UserProfile") return await SyncUserProfileAsync(db, op);
        return false;
    }

    private async Task<bool> SyncUserAvatarAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var payload = JsonSerializer.Deserialize<UploadAvatarRequest>(op.Payload, _jsonOptions);
        if (payload?.AvatarData is null || payload.AvatarData.Length == 0) return true;
        var ok = await _api.UploadUserAvatarAsync(op.EntityId, payload.AvatarData);
        if (ok)
        {
            var user = await db.Users.FindAsync(op.EntityId);
            if (user is not null) user.IsSynced = true;
        }
        return ok;
    }

    private async Task<bool> SyncUserProfileAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var req = JsonSerializer.Deserialize<UpdateUserRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        var updated = await _api.UpdateUserAsync(op.EntityId, req);
        if (updated is null) return false;
        
        var user = await db.Users.FindAsync(op.EntityId);
        if (user is not null)
        {
            user.IsSynced = true;
            user.LastModifiedLocally = DateTime.UtcNow;
        }

        return true;
    }
}
