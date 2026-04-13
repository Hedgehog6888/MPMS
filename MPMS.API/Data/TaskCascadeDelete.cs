using Microsoft.EntityFrameworkCore;

namespace MPMS.API.Data;

/// <summary>
/// Полное удаление задачи и зависимостей. Простой <see cref="DbSet{TEntity}.Remove"/> не проходит из‑за FK с NoAction
/// (обсуждения, файлы задачи, зависимости «другие задачи → эта», движения склада, история оборудования).
/// </summary>
public static class TaskCascadeDelete
{
    public static async Task<bool> TryDeleteTaskGraphAsync(ApplicationDbContext db, Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.Tasks.AnyAsync(t => t.Id == taskId, cancellationToken);
        if (!exists)
            return false;

        var stageIds = await db.TaskStages.Where(s => s.TaskId == taskId).Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (stageIds.Count > 0)
        {
            await db.Files.Where(f => f.StageId.HasValue && stageIds.Contains(f.StageId.Value))
                .ExecuteDeleteAsync(cancellationToken);
            await db.StageMaterials.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync(cancellationToken);
            await db.StageServices.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync(cancellationToken);
            await db.StageAssignees.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync(cancellationToken);
            await db.TaskStages.Where(x => stageIds.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        await db.TaskDependencies.Where(d => d.TaskId == taskId || d.DependsOnTaskId == taskId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.TaskAssignees.Where(x => x.TaskId == taskId).ExecuteDeleteAsync(cancellationToken);
        await db.DiscussionMessages.Where(m => m.TaskId == taskId).ExecuteDeleteAsync(cancellationToken);
        await db.Files.Where(f => f.TaskId == taskId).ExecuteDeleteAsync(cancellationToken);

        await db.MaterialStockMovements.Where(x => x.TaskId == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TaskId, (Guid?)null), cancellationToken);
        await db.EquipmentHistoryEntries.Where(x => x.TaskId == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TaskId, (Guid?)null), cancellationToken);
        await db.Equipments.Where(e => e.CheckedOutTaskId == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CheckedOutTaskId, (Guid?)null), cancellationToken);

        await db.Tasks.Where(t => t.Id == taskId).ExecuteDeleteAsync(cancellationToken);
        return true;
    }
}
