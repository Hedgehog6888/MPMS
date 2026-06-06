using System.IO;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

/// <summary>Фильтр файлов, доступных в общем списке (без закрытых проектов).</summary>
public static class AvailableFilesQuery
{
    public static async Task<IQueryable<LocalFile>> ApplyGlobalFilterAsync(
        IQueryable<LocalFile> query,
        LocalDbContext db,
        IAuthService auth)
    {
        var userRole = auth.UserRole;
        var userId = auth.UserId;

        if (userRole is "Administrator" or "Admin")
        {
            var closedProjectIds = await db.Projects.AsNoTracking()
                .Where(p => p.IsClosed)
                .Select(p => p.Id)
                .ToListAsync();

            // Получаем все этапы незакрытых проектов
            var adminStageIds = await db.TaskStages.AsNoTracking()
                .Join(db.Tasks.AsNoTracking(), s => s.TaskId, t => t.Id, (s, t) => new { s.Id, t.ProjectId })
                .Where(x => !closedProjectIds.Contains(x.ProjectId))
                .Select(x => x.Id)
                .ToListAsync();

            return query.Where(f =>
                (!f.ProjectId.HasValue || !closedProjectIds.Contains(f.ProjectId.Value)) &&
                (!f.StageId.HasValue || adminStageIds.Contains(f.StageId.Value)));
        }

        if (userRole is "Worker" or "Работник" && userId.HasValue)
        {
            var workerTaskIds = await db.Tasks.AsNoTracking()
                .Where(t => t.AssignedUserId == userId.Value)
                .Select(t => t.Id)
                .ToListAsync();
            var assigneeTaskIds = await db.TaskAssignees.AsNoTracking()
                .Where(ta => ta.UserId == userId.Value)
                .Select(ta => ta.TaskId)
                .ToListAsync();
            var allTaskIds = workerTaskIds.Concat(assigneeTaskIds).Distinct().ToHashSet();

            var workerStageIds = await db.TaskStages.AsNoTracking()
                .Where(s => s.AssignedUserId == userId.Value)
                .Select(s => s.Id)
                .ToListAsync();
            var assigneeStageIds = await db.StageAssignees.AsNoTracking()
                .Where(sa => sa.UserId == userId.Value)
                .Select(sa => sa.StageId)
                .ToListAsync();
            var allStageIds = workerStageIds.Concat(assigneeStageIds).Distinct().ToHashSet();

            if (allTaskIds.Count > 0)
            {
                var taskStageIds = await db.TaskStages.AsNoTracking()
                    .Where(s => allTaskIds.Contains(s.TaskId))
                    .Select(s => s.Id)
                    .ToListAsync();
                foreach (var id in taskStageIds) allStageIds.Add(id);
            }

            if (allTaskIds.Count == 0 && allStageIds.Count == 0)
                return query.Where(_ => false);

            return query.Where(f =>
                (f.TaskId.HasValue && allTaskIds.Contains(f.TaskId.Value)) ||
                (f.StageId.HasValue && allStageIds.Contains(f.StageId.Value)));
        }

        if (userId is null)
            return query.Where(_ => false);

        var userProjectIds = await db.ProjectMembers.AsNoTracking()
            .Where(pm => pm.UserId == userId)
            .Select(pm => pm.ProjectId)
            .ToListAsync();

        var closedIds = await db.Projects.AsNoTracking()
            .Where(p => p.IsClosed)
            .Select(p => p.Id)
            .ToListAsync();

        // Получаем все этапы проектов пользователя, которые не закрыты
        var availableStageIds = await db.TaskStages.AsNoTracking()
            .Join(db.Tasks.AsNoTracking(), s => s.TaskId, t => t.Id, (s, t) => new { s.Id, t.ProjectId })
            .Where(x => userProjectIds.Contains(x.ProjectId) && !closedIds.Contains(x.ProjectId))
            .Select(x => x.Id)
            .ToListAsync();

        return query.Where(f =>
            (!f.ProjectId.HasValue ||
             (userProjectIds.Contains(f.ProjectId.Value) && !closedIds.Contains(f.ProjectId.Value))) &&
            (!f.StageId.HasValue || availableStageIds.Contains(f.StageId.Value)));
    }

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".svg"];

    public static bool IsImageFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLower();
        return ext != null && ImageExtensions.Contains(ext);
    }
}
