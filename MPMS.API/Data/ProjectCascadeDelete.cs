using Microsoft.EntityFrameworkCore;

namespace MPMS.API.Data;

/// <summary>
/// Полное удаление проекта и зависимостей на сервере (локальный клиент делает то же в SQLite).
/// Простой Remove(Project) не проходит из‑за FK с NoAction (обсуждения, файлы этапов, связи задач).
/// </summary>
public static class ProjectCascadeDelete
{
    public static async Task<bool> TryDeleteProjectGraphAsync(ApplicationDbContext db, Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var projectExists = await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken);
        if (!projectExists)
            return false;

        var taskIds = await db.Tasks.Where(t => t.ProjectId == projectId).Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tid in taskIds)
            await TaskCascadeDelete.TryDeleteTaskGraphAsync(db, tid, cancellationToken);

        await db.DiscussionMessages.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.Files.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await db.ProjectMembers.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        await db.MaterialStockMovements.Where(x => x.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProjectId, (Guid?)null), cancellationToken);

        await db.EquipmentHistoryEntries.Where(x => x.ProjectId == projectId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProjectId, (Guid?)null), cancellationToken);

        await db.Equipments.Where(e => e.CheckedOutProjectId == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.CheckedOutProjectId, (Guid?)null)
                .SetProperty(e => e.CheckedOutTaskId, (Guid?)null), cancellationToken);

        await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(cancellationToken);
        return true;
    }
}
