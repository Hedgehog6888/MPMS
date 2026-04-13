using Microsoft.EntityFrameworkCore;
using MPMS.Data;

namespace MPMS.Services;

/// <summary>Жёсткое удаление связанных локальных данных (как при «удалить навсегда» и при подтягивании снимка с сервера).</summary>
public static class LocalDbGraphDeletion
{
    public static async Task PermanentlyDeleteProjectGraphAsync(LocalDbContext db, Guid projectId)
    {
        var taskIds = await db.Tasks.Where(t => t.ProjectId == projectId).Select(t => t.Id).ToListAsync();
        var stageIds = taskIds.Count == 0
            ? []
            : await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).Select(s => s.Id).ToListAsync();

        if (stageIds.Count > 0)
        {
            await db.StageMaterials.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.StageServices.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.StageEquipments.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.StageAssignees.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.TaskStages.Where(x => stageIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        if (taskIds.Count > 0)
        {
            await db.TaskAssignees.Where(x => taskIds.Contains(x.TaskId)).ExecuteDeleteAsync();
            await db.Messages.Where(x => x.TaskId.HasValue && taskIds.Contains(x.TaskId.Value)).ExecuteDeleteAsync();
            await db.Files.Where(x => x.TaskId.HasValue && taskIds.Contains(x.TaskId.Value)).ExecuteDeleteAsync();
            await db.Tasks.Where(x => taskIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        await db.Messages.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync();
        await db.Files.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync();
        await db.ProjectMembers.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync();
        await db.Projects.Where(x => x.Id == projectId).ExecuteDeleteAsync();
    }

    public static async Task PermanentlyDeleteTaskGraphAsync(LocalDbContext db, Guid taskId)
    {
        var stageIds = await db.TaskStages.Where(s => s.TaskId == taskId).Select(s => s.Id).ToListAsync();
        if (stageIds.Count > 0)
        {
            await db.StageMaterials.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.StageServices.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.StageEquipments.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.StageAssignees.Where(x => stageIds.Contains(x.StageId)).ExecuteDeleteAsync();
            await db.TaskStages.Where(x => stageIds.Contains(x.Id)).ExecuteDeleteAsync();
        }

        await db.TaskAssignees.Where(x => x.TaskId == taskId).ExecuteDeleteAsync();
        await db.Messages.Where(x => x.TaskId == taskId).ExecuteDeleteAsync();
        await db.Files.Where(x => x.TaskId == taskId).ExecuteDeleteAsync();
        await db.Tasks.Where(x => x.Id == taskId).ExecuteDeleteAsync();
    }

    public static async Task PermanentlyDeleteStageGraphAsync(LocalDbContext db, Guid stageId)
    {
        await db.StageMaterials.Where(x => x.StageId == stageId).ExecuteDeleteAsync();
        await db.StageServices.Where(x => x.StageId == stageId).ExecuteDeleteAsync();
        await db.StageEquipments.Where(x => x.StageId == stageId).ExecuteDeleteAsync();
        await db.StageAssignees.Where(x => x.StageId == stageId).ExecuteDeleteAsync();
        await db.TaskStages.Where(x => x.Id == stageId).ExecuteDeleteAsync();
    }

    public static async Task PermanentlyDeleteMaterialGraphAsync(LocalDbContext db, Guid materialId)
    {
        await db.MaterialStockMovements.Where(x => x.MaterialId == materialId).ExecuteDeleteAsync();
        await db.StageMaterials.Where(x => x.MaterialId == materialId).ExecuteDeleteAsync();
        await db.Materials.Where(x => x.Id == materialId).ExecuteDeleteAsync();
    }

    public static async Task PermanentlyDeleteEquipmentGraphAsync(LocalDbContext db, Guid equipmentId)
    {
        await db.EquipmentHistoryEntries.Where(x => x.EquipmentId == equipmentId).ExecuteDeleteAsync();
        await db.StageEquipments.Where(x => x.EquipmentId == equipmentId).ExecuteDeleteAsync();
        await db.Equipments.Where(x => x.Id == equipmentId).ExecuteDeleteAsync();
    }
}
