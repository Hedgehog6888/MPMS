using Microsoft.EntityFrameworkCore;
using MPMS.Data;

namespace MPMS.Services;

/// <summary>Правила карточки участника: админ/менеджер — любой; прораб — только работники; работник — не открывает.</summary>
public static class UserPeekAccess
{
    public static string? ResolveViewerRole(IAuthService auth, LocalDbContext db)
    {
        var r = auth.UserRole;
        if (!string.IsNullOrWhiteSpace(r))
            return r.Trim();
        if (auth.UserId is { } uid)
        {
            return db.Users.AsNoTracking()
                .Where(u => u.Id == uid)
                .Select(u => u.RoleName)
                .FirstOrDefault();
        }
        return null;
    }

    public static bool IsAdministrator(string? role) =>
        role is not null
        && (role.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Admin", StringComparison.OrdinalIgnoreCase));

    public static bool IsManager(string? role) =>
        role is not null
        && (role.Equals("Project Manager", StringComparison.OrdinalIgnoreCase)
            || role.Equals("ProjectManager", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Manager", StringComparison.OrdinalIgnoreCase));

    public static bool IsForeman(string? role) =>
        role is not null
        && (role.Equals("Foreman", StringComparison.OrdinalIgnoreCase)
            || role == "Прораб");

    public static bool IsWorker(string? role) =>
        role is not null
        && (role.Equals("Worker", StringComparison.OrdinalIgnoreCase)
            || role == "Работник");

    public static bool IsTargetWorkerRole(string? roleName) => IsWorker(roleName);

    /// <summary>Кликабельная строка исполнителя (курсор-рука): работнику никогда; прорабу — только если назначенный работник.</summary>
    public static bool CanInteractPeekRow(IAuthService auth, LocalDbContext db, string? assigneeRoleName)
    {
        var vr = ResolveViewerRole(auth, db);
        if (IsWorker(vr))
            return false;
        if (IsAdministrator(vr) || IsManager(vr))
            return true;
        if (IsForeman(vr))
            return IsTargetWorkerRole(assigneeRoleName);
        return false;
    }

    /// <summary>Можно ли открыть оверлей для указанного пользователя.</summary>
    public static bool CanViewerPeekTargetUser(IAuthService auth, LocalDbContext db, Guid targetUserId)
    {
        var vr = ResolveViewerRole(auth, db);
        if (IsWorker(vr))
            return false;
        if (IsAdministrator(vr) || IsManager(vr))
            return true;
        if (IsForeman(vr))
        {
            var tr = db.Users.AsNoTracking()
                .Where(u => u.Id == targetUserId)
                .Select(u => u.RoleName)
                .FirstOrDefault();
            return IsTargetWorkerRole(tr);
        }
        return false;
    }

    public static async Task<bool> ViewerCanAccessProjectAsync(
        LocalDbContext db, Guid viewerId, IAuthService auth, Guid projectId)
    {
        var vr = ResolveViewerRole(auth, db);

        if (IsAdministrator(vr))
            return await db.Projects.AnyAsync(p => p.Id == projectId && !p.IsArchived);

        if (IsManager(vr))
        {
            return await db.Projects.AnyAsync(p =>
                p.Id == projectId && !p.IsArchived && p.ManagerId == viewerId);
        }

        if (IsForeman(vr))
        {
            return await db.ProjectMembers
                .AnyAsync(m => m.ProjectId == projectId && m.UserId == viewerId);
        }

        if (IsWorker(vr))
        {
            var hasTask = await db.Tasks.AnyAsync(t => t.ProjectId == projectId && !t.IsArchived
                && (t.AssignedUserId == viewerId
                    || db.TaskAssignees.Any(ta => ta.TaskId == t.Id && ta.UserId == viewerId)));
            if (hasTask)
                return true;

            return await db.TaskStages
                .Where(s => !s.IsArchived
                    && db.Tasks.Any(t => t.Id == s.TaskId && t.ProjectId == projectId && !t.IsArchived))
                .AnyAsync(s => s.AssignedUserId == viewerId
                    || db.StageAssignees.Any(sa => sa.StageId == s.Id && sa.UserId == viewerId));
        }

        return false;
    }

    /// <summary>Проекты, в контексте которых зитель может видеть данные участника.</summary>
    public static async Task<HashSet<Guid>> GetViewerAccessibleProjectIdsAsync(
        LocalDbContext db, Guid viewerId, IAuthService auth, CancellationToken ct = default)
    {
        var vr = ResolveViewerRole(auth, db);

        if (IsAdministrator(vr))
        {
            return (await db.Projects.AsNoTracking()
                .Where(p => !p.IsArchived)
                .Select(p => p.Id)
                .ToListAsync(ct)).ToHashSet();
        }

        if (IsManager(vr))
        {
            return (await db.Projects.AsNoTracking()
                .Where(p => !p.IsArchived && p.ManagerId == viewerId)
                .Select(p => p.Id)
                .ToListAsync(ct)).ToHashSet();
        }

        if (IsForeman(vr))
        {
            return (await db.ProjectMembers.AsNoTracking()
                .Where(m => m.UserId == viewerId)
                .Select(m => m.ProjectId)
                .ToListAsync(ct)).ToHashSet();
        }

        if (IsWorker(vr))
        {
            var fromTasks = await db.Tasks.AsNoTracking()
                .Where(t => !t.IsArchived
                    && (t.AssignedUserId == viewerId
                        || db.TaskAssignees.Any(ta => ta.TaskId == t.Id && ta.UserId == viewerId)))
                .Select(t => t.ProjectId)
                .Distinct()
                .ToListAsync(ct);

            var fromStages = await (
                from s in db.TaskStages.AsNoTracking()
                join t in db.Tasks.AsNoTracking() on s.TaskId equals t.Id
                where !s.IsArchived && !t.IsArchived
                      && (s.AssignedUserId == viewerId
                          || db.StageAssignees.Any(sa => sa.StageId == s.Id && sa.UserId == viewerId))
                select t.ProjectId).Distinct().ToListAsync(ct);

            return fromTasks.Concat(fromStages).ToHashSet();
        }

        return [];
    }
}
