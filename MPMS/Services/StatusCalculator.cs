using MPMS.Models;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Services;

/// <summary>Единый расчёт статусов задачи и проекта по этапам/задачам. Синхронизация везде.</summary>
public static class StatusCalculator
{
    public static TaskStatus GetTaskStatusFromStages(IEnumerable<LocalTaskStage> stages)
    {
        var list = stages.ToList();
        if (list.Count == 0) return TaskStatus.Planned;
        if (list.All(s => s.Status == StageStatus.Completed)) return TaskStatus.Completed;
        if (list.Any(s => s.Status == StageStatus.InProgress) || list.Any(s => s.Status == StageStatus.Completed))
            return TaskStatus.InProgress;
        return TaskStatus.Planned;
    }

    public static ProjectStatus GetProjectStatusFromTasks(IEnumerable<LocalTask> tasks)
    {
        var list = tasks.Where(t => !t.IsMarkedForDeletion).ToList();
        if (list.Count == 0) return ProjectStatus.Planning;
        if (list.All(t => t.Status == TaskStatus.Completed)) return ProjectStatus.Completed;
        if (list.Any(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Paused || t.Status == TaskStatus.Completed))
            return ProjectStatus.InProgress;
        return ProjectStatus.Planning;
    }
}
