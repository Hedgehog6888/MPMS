using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.Services;

/// <summary>Filters activity log by role: Admin=all, Manager=all except admin, Foreman=self+collaborators, Worker=self only.</summary>
public static class ActivityFilterService
{
    /// <summary>Action types shown only in admin History/Activity tabs (excluded from recent activity).</summary>
    private static readonly HashSet<string> AdminOnlyEventKinds = new()
    {
        ActivityActionKind.Login, ActivityActionKind.Logout,
        ActivityActionKind.PasswordChanged, ActivityActionKind.AvatarChanged,
        ActivityActionKind.UserCreated, ActivityActionKind.UserEdited, ActivityActionKind.UserDeleted,
        ActivityActionKind.UserBlocked, ActivityActionKind.UserUnblocked
    };

    public static async Task<List<LocalActivityLog>> GetFilteredActivitiesAsync(
        LocalDbContext db, IAuthService auth, int take = 10, bool excludeAuthEvents = true, CancellationToken ct = default)
    {
        var userRole = auth.UserRole ?? "";
        var currentUserId = auth.UserId;

        IQueryable<LocalActivityLog> query = db.ActivityLogs.OrderByDescending(a => a.CreatedAt);
        if (excludeAuthEvents)
            query = query.Where(a => a.ActionType == null || !AdminOnlyEventKinds.Contains(a.ActionType));

        // Admin: no post-filter — a single query is enough
        if (IsAdminRole(userRole))
        {
            var adminList = await query.Take(take).ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            await AttachAvatarsAsync(db, adminList, ct);
            return adminList;
        }

        // Other roles: filtering can drop most recent rows (e.g. many admin actions, then login as manager).
        // Scan chronologically in batches until we have `take` visible entries or hit a scan cap.
        Dictionary<Guid, string>? userRolesById = null;
        if (IsManagerRole(userRole))
        {
            userRolesById = await db.Users
                .Select(u => new { u.Id, u.RoleName })
                .ToDictionaryAsync(u => u.Id, u => u.RoleName, ct);
            ct.ThrowIfCancellationRequested();
        }

        HashSet<Guid>? foremanVisibleIds = null;
        if (IsForemanRole(userRole) && currentUserId.HasValue)
        {
            foremanVisibleIds = await GetForemanVisibleUserIdsAsync(db, currentUserId.Value, ct);
            ct.ThrowIfCancellationRequested();
        }

        const int batchSize = 150;
        var maxScan = Math.Min(20_000, Math.Max(2_000, take * 100));
        var result = new List<LocalActivityLog>(Math.Min(take, 32));
        var skip = 0;

        while (result.Count < take && skip < maxScan)
        {
            var batch = await query.Skip(skip).Take(batchSize).ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (batch.Count == 0)
                break;
            skip += batch.Count;

            foreach (var a in batch)
            {
                if (result.Count >= take)
                    break;
                if (PassesRoleFilter(a, userRole, currentUserId, userRolesById, foremanVisibleIds))
                    result.Add(a);
            }
        }

        await AttachAvatarsAsync(db, result, ct);
        return result;
    }

    private static bool PassesRoleFilter(
        LocalActivityLog a,
        string userRole,
        Guid? currentUserId,
        Dictionary<Guid, string>? userRolesById,
        HashSet<Guid>? foremanVisibleIds)
    {
        if (IsManagerRole(userRole))
        {
            var actorRole = a.ActorRole
                ?? (a.UserId.HasValue && userRolesById is not null
                    ? userRolesById.GetValueOrDefault(a.UserId.Value)
                    : null);
            return actorRole != null && !IsAdminRole(actorRole);
        }

        if (IsForemanRole(userRole))
        {
            if (!currentUserId.HasValue || foremanVisibleIds is null)
                return true;
            return a.UserId.HasValue && foremanVisibleIds.Contains(a.UserId.Value);
        }

        if (currentUserId.HasValue)
            return a.UserId == currentUserId.Value;

        return true;
    }

    private static async Task AttachAvatarsAsync(LocalDbContext db, List<LocalActivityLog> activities, CancellationToken ct)
    {
        var userIds = activities.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().ToList();
        if (userIds.Count == 0)
            return;

        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
            .ToDictionaryAsync(u => u.Id, ct);
        ct.ThrowIfCancellationRequested();

        foreach (var a in activities)
        {
            if (a.UserId.HasValue && userAvatars.TryGetValue(a.UserId.Value, out var av))
            {
                a.AvatarData = av.AvatarData;
                a.AvatarPath = av.AvatarPath;
            }
        }
    }

    /// <summary>Returns the count of activities visible to the current user (for stats display). Uses a reasonable limit.</summary>
    public static async Task<int> GetFilteredActivityCountAsync(
        LocalDbContext db, IAuthService auth, bool excludeAuthEvents = true, CancellationToken ct = default)
    {
        var activities = await GetFilteredActivitiesAsync(db, auth, 500, excludeAuthEvents, ct);
        return activities.Count;
    }

    public static bool IsAdminRole(string? role) =>
        !string.IsNullOrEmpty(role) &&
        (string.Equals(role, "Administrator", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase));

    private static bool IsManagerRole(string role) =>
        string.Equals(role, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);

    private static bool IsForemanRole(string role) =>
        string.Equals(role, "Foreman", StringComparison.OrdinalIgnoreCase);

    private static async Task<HashSet<Guid>> GetForemanVisibleUserIdsAsync(LocalDbContext db, Guid foremanUserId, CancellationToken ct)
    {
        var visible = new HashSet<Guid> { foremanUserId };

        var foremanProjectIds = await db.ProjectMembers
            .Where(m => m.UserId == foremanUserId)
            .Select(m => m.ProjectId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (foremanProjectIds.Count == 0)
            return visible;

        var memberIds = await db.ProjectMembers
            .Where(m => foremanProjectIds.Contains(m.ProjectId))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var id in memberIds) visible.Add(id);

        var taskIds = await db.Tasks
            .Where(t => foremanProjectIds.Contains(t.ProjectId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        if (taskIds.Count > 0)
        {
            var taskAssigneeIds = await db.TaskAssignees
                .Where(ta => taskIds.Contains(ta.TaskId))
                .Select(ta => ta.UserId)
                .ToListAsync(ct);
            foreach (var id in taskAssigneeIds) visible.Add(id);

            var stageIds = await db.TaskStages
                .Where(s => taskIds.Contains(s.TaskId))
                .Select(s => s.Id)
                .ToListAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (stageIds.Count > 0)
            {
                var stageAssigneeIds = await db.StageAssignees
                    .Where(sa => stageIds.Contains(sa.StageId))
                    .Select(sa => sa.UserId)
                    .ToListAsync(ct);
                foreach (var id in stageAssigneeIds) visible.Add(id);
            }
        }

        return visible;
    }
}
