using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

/// <summary>Ответ GET api/users → локальная таблица Users.</summary>
public static class UserListMergeHelper
{
    public static async Task ApplyPulledUsersAsync(
        LocalDbContext db, IReadOnlyList<UserResponse> users, IAuthService auth)
    {
        var deletedIds = (await db.DeletedUserIds.Select(x => x.Id).ToListAsync()).ToHashSet();
        var existing = await db.Users.ToDictionaryAsync(u => u.Id);

        foreach (var u in users)
        {
            // Локально помечали «удалён навсегда» (офлайн и т.п.). Если сервер всё ещё отдаёт
            // пользователя в списке — источник правды на сервере: снимаем tombstone и подтягиваем строку.
            if (deletedIds.Contains(u.Id))
            {
                var tomb = await db.DeletedUserIds.FindAsync(u.Id);
                if (tomb is not null)
                    db.DeletedUserIds.Remove(tomb);
                deletedIds.Remove(u.Id);
            }

            var fullName = $"{u.FirstName} {u.LastName}".Trim();
            if (existing.TryGetValue(u.Id, out var local))
            {
                var keepPasswordHash = local.PasswordHash;
                local.Name = fullName;
                local.FirstName = u.FirstName;
                local.LastName = u.LastName;
                local.Username = u.Username;
                local.Email = u.Email;
                local.RoleName = u.Role;
                local.RoleId = u.RoleId;
                local.SubRole = u.SubRole;
                local.AdditionalSubRoles = u.AdditionalSubRoles;
                local.BirthDate = u.BirthDate;
                local.HomeAddress = u.HomeAddress;
                local.AvatarPath = null;
                local.AvatarData = u.AvatarData;
                local.CreatedAt = u.CreatedAt;
                local.IsBlocked = u.IsBlocked;
                local.BlockedAt = u.BlockedAt;
                local.BlockedReason = u.BlockedReason;
                local.IsSynced = true;
                local.PasswordHash = keepPasswordHash;
            }
            else
            {
                db.Users.Add(new LocalUser
                {
                    Id = u.Id, Name = fullName,
                    FirstName = u.FirstName, LastName = u.LastName,
                    Username = u.Username, Email = u.Email, RoleName = u.Role,
                    RoleId = u.RoleId,
                    SubRole = u.SubRole,
                    AdditionalSubRoles = u.AdditionalSubRoles,
                    BirthDate = u.BirthDate,
                    HomeAddress = u.HomeAddress,
                    AvatarData = u.AvatarData,
                    IsSynced = true, CreatedAt = u.CreatedAt,
                    IsBlocked = u.IsBlocked,
                    BlockedAt = u.BlockedAt,
                    BlockedReason = u.BlockedReason
                });
            }
        }

        if (users.Count > 0)
        {
            var serverIds = users.Select(u => u.Id).ToHashSet();
            var currentId = auth.UserId;
            foreach (var row in await db.Users.Where(l => !serverIds.Contains(l.Id)).ToListAsync())
            {
                if (currentId.HasValue && row.Id == currentId.Value) continue;
                db.Users.Remove(row);
            }
        }

        await db.PendingOperations.Where(p => p.EntityType == "UserProfile").ExecuteDeleteAsync();
    }
}
