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

        var allActivities = await query.Take(50).ToListAsync(ct);
        ct.ThrowIfCancellationRequested();

        // Admin: see all
        if (IsAdminRole(userRole))
            return allActivities.Take(take).ToList();

        // Manager: exclude actions by admins (including when role is unknown — treat as admin for safety)
        if (IsManagerRole(userRole))
        {
            var userRoles = await db.Users
                .Select(u => new { u.Id, u.RoleName })
                .ToDictionaryAsync(u => u.Id, u => u.RoleName, ct);
            ct.ThrowIfCancellationRequested();

            return allActivities
                .Where(a =>
                {
                    var actorRole = a.ActorRole ?? (a.UserId.HasValue ? userRoles.GetValueOrDefault(a.UserId.Value) : null);
                    return actorRole != null && !IsAdminRole(actorRole);
                })
                .Take(take)
                .ToList();
        }

        // Foreman: only own + collaborators — exclude activities without UserId (could be admin/manager)
        if (IsForemanRole(userRole) && currentUserId.HasValue)
        {
            var visibleUserIds = await GetForemanVisibleUserIdsAsync(db, currentUserId.Value, ct);
            return allActivities
                .Where(a => a.UserId.HasValue && visibleUserIds.Contains(a.UserId.Value))
                .Take(take)
                .ToList();
        }

        // Worker and others: only own actions
        if (currentUserId.HasValue)
        {
            return allActivities
                .Where(a => a.UserId == currentUserId.Value)
                .Take(take)
                .ToList();
        }

        return allActivities.Take(take).ToList();
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
