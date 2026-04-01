using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Services;

/// <summary>Единая формула прогресса: учитывает статусы, этапы, средний прогресс и просрочку.</summary>
public static class ProgressCalculator
{
    /// <summary>Вес этапа в доле «объёма»: завершён = 1, в работе ≈ половина пути (согласовано с 0.58 в агрегате).</summary>
    private const double InProgressStagePartial = 0.55;

    private static double GetStageWeight(StageStatus status) => status switch
    {
        StageStatus.Completed => 1.00,
        StageStatus.InProgress => 0.58,
        _ => 0.08
    };

    private static double GetTaskWeight(TaskStatus status) => status switch
    {
        TaskStatus.Completed => 1.00,
        TaskStatus.InProgress => 0.65,
        TaskStatus.Paused => 0.32,
        _ => 0.08
    };

    private static double NormalizeTaskStatusWeight(TaskStatus status) =>
        Math.Clamp(GetTaskWeight(status), 0, 1);

    /// <summary>Этап не входит в прогресс задачи (архив / пометка этапа). Пометка задачи не обнуляет этапы — % на плашке сохраняется; помеченные задачи не попадают в статистику проекта в ApplyProjectMetrics.</summary>
    private static bool StageExcludedFromTaskProgress(LocalTaskStage s)
    {
        if (s.IsArchived || s.IsMarkedForDeletion) return true;
        return false;
    }

    public static void ApplyTaskMetrics(LocalTask task, IReadOnlyCollection<LocalTaskStage> stages)
    {
        var activeStages = stages
            .Where(s => !StageExcludedFromTaskProgress(s))
            .ToList();

        task.TotalStages = activeStages.Count;
        task.CompletedStages = activeStages.Count(s => s.Status == StageStatus.Completed);
        task.InProgressStages = activeStages.Count(s => s.Status == StageStatus.InProgress);

        task.Status = activeStages.Count > 0
            ? StatusCalculator.GetTaskStatusFromStages(activeStages)
            : task.Status switch
            {
                TaskStatus.Completed => TaskStatus.Completed,
                TaskStatus.Paused => TaskStatus.Paused,
                TaskStatus.InProgress => TaskStatus.InProgress,
                _ => TaskStatus.Planned
            };
    }

    public static void ApplyProjectMetrics(LocalProject project, IReadOnlyCollection<LocalTask> tasks, IReadOnlyCollection<LocalTaskStage> stages)
    {
        var activeTasks = tasks
            .Where(t => !t.IsArchived && !t.IsMarkedForDeletion)
            .ToList();
        var taskIds = activeTasks.Select(t => t.Id).ToHashSet();
        var activeStages = stages
            .Where(s => taskIds.Contains(s.TaskId)
                        && !s.IsArchived
                        && !StageExcludedFromTaskProgress(s))
            .ToList();

        project.TotalTasks = activeTasks.Count;
        project.CompletedTasks = activeTasks.Count(t => t.Status == TaskStatus.Completed);
        project.InProgressTasks = activeTasks.Count(t => t.Status == TaskStatus.InProgress);
        project.PausedTasks = activeTasks.Count(t => t.Status == TaskStatus.Paused);
        project.OverdueTasks = activeTasks.Count(t => t.IsOverdue);
        project.TotalStages = activeStages.Count;
        project.CompletedStages = activeStages.Count(s => s.Status == StageStatus.Completed);
        project.InProgressStages = activeStages.Count(s => s.Status == StageStatus.InProgress);
        project.AverageTaskProgress = activeTasks.Count == 0 ? 0 : activeTasks.Average(t => t.ProgressPercent);
        project.Status = StatusCalculator.GetProjectStatusFromTasks(activeTasks);
    }

    public static int GetTaskProgressPercent(LocalTask task)
    {
        if (task.TotalStages <= 0)
        {
            return task.Status == TaskStatus.Completed ? 100 : 0;
        }

        // Все этапы ещё «Запланировано» — без фактического хода работ прогресс 0% (не «паразитные» 5–8%).
        if (task.CompletedStages == 0 && task.InProgressStages == 0)
            return 0;

        double total = task.TotalStages;
        // Доля объёма: завершённые + частичный зачёт «в работе» (без двойного учёта с completionRatio).
        double volumeProgress = (task.CompletedStages + InProgressStagePartial * task.InProgressStages) / total;
        double stageStatusScore = (
            task.CompletedStages * GetStageWeight(StageStatus.Completed) +
            task.InProgressStages * GetStageWeight(StageStatus.InProgress) +
            task.PlannedStages * GetStageWeight(StageStatus.Planned)) / total;
        double statusNorm = NormalizeTaskStatusWeight(task.Status);

        // Веса подобраны так, чтобы сохранить прежний порядок величин (~калибровка старой смеси 55/25/10/10),
        // но убрать избыточное дублирование completionRatio + activeRatio вместе со stageStatusScore.
        double raw = (
            stageStatusScore * 0.52 +
            volumeProgress * 0.33 +
            statusNorm * 0.15) * 100;

        if (task.IsOverdue && task.Status != TaskStatus.Completed)
            raw -= 12;

        if (task.Status == TaskStatus.Completed && task.CompletedStages >= task.TotalStages)
            raw = 100;

        return (int)Math.Round(Math.Clamp(raw, 0, 100), MidpointRounding.AwayFromZero);
    }

    public static int GetProjectProgressPercent(LocalProject project)
    {
        if (project.IsMarkedForDeletion)
            return 0;

        if (project.TotalTasks <= 0)
            return 0;

        // Ни одна задача не в работе/на паузе/не завершена и нет активных этапов — только «запланировано» → 0%.
        if (project.CompletedTasks == 0
            && project.InProgressTasks == 0
            && project.PausedTasks == 0
            && project.InProgressStages == 0)
            return 0;

        double totalTasks = project.TotalTasks;
        double plannedTasks = Math.Max(0, project.TotalTasks - project.CompletedTasks - project.InProgressTasks - project.PausedTasks);
        double taskStatusScore = (
            project.CompletedTasks * GetTaskWeight(TaskStatus.Completed) +
            project.InProgressTasks * GetTaskWeight(TaskStatus.InProgress) +
            project.PausedTasks * GetTaskWeight(TaskStatus.Paused) +
            plannedTasks * GetTaskWeight(TaskStatus.Planned)) / totalTasks;

        double completionScore = project.CompletedTasks / totalTasks;
        double averageTaskScore = Math.Clamp(project.AverageTaskProgress / 100d, 0, 1);

        double stageAggregateScore = averageTaskScore;
        if (project.TotalStages > 0)
        {
            double plannedStages = Math.Max(0, project.TotalStages - project.CompletedStages - project.InProgressStages);
            stageAggregateScore = (
                project.CompletedStages * GetStageWeight(StageStatus.Completed) +
                project.InProgressStages * GetStageWeight(StageStatus.InProgress) +
                plannedStages * GetStageWeight(StageStatus.Planned)) / project.TotalStages;
        }

        // Один «сигнал работы»: этапы по проекту + средний % задач (важно для задач без этапов в том же проекте).
        double workScore = project.TotalStages > 0
            ? Math.Clamp(0.62 * stageAggregateScore + 0.38 * averageTaskScore, 0, 1)
            : averageTaskScore;

        double overduePenalty = project.OverdueTasks <= 0
            ? 0
            : Math.Min(0.18, (project.OverdueTasks / totalTasks) * 0.18);

        double raw = (
            taskStatusScore * 0.28 +
            workScore * 0.57 +
            completionScore * 0.15) * 100;

        raw -= overduePenalty * 100;

        if (project.CompletedTasks == project.TotalTasks)
            raw = 100;
        else if (project.CompletedTasks == 0 && project.InProgressTasks == 0 && project.InProgressStages == 0 && project.PausedTasks > 0)
            raw = Math.Min(raw, 12);

        return (int)Math.Round(Math.Clamp(raw, 0, 100), MidpointRounding.AwayFromZero);
    }

    /// <summary>Строка для отображения: "67.5%" или "100%"</summary>
    public static string FormatPercent(double value)
    {
        return value % 1 == 0 ? $"{(int)value}%" : $"{value:F1}%";
    }

    /// <summary>
    /// Загружает активные этапы из БД и обновляет счётчики/прогресс задачи.
    /// Нужно после <c>FindAsync</c>, иначе <see cref="LocalTask.ProgressPercent"/> и этапы остаются устаревшими или нулевыми.
    /// </summary>
    public static async System.Threading.Tasks.Task ApplyTaskMetricsForTaskAsync(
        LocalDbContext db, LocalTask task, System.Threading.CancellationToken ct = default)
    {
        var projMarked = await db.Projects.AsNoTracking()
            .Where(p => p.Id == task.ProjectId)
            .Select(p => p.IsMarkedForDeletion)
            .FirstOrDefaultAsync(ct);
        task.ProjectIsMarkedForDeletion = projMarked;

        var stages = await db.TaskStages
            .Where(s => s.TaskId == task.Id && !s.IsArchived)
            .ToListAsync(ct);
        foreach (var s in stages)
        {
            s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
            s.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;
        }

        ApplyTaskMetrics(task, stages);
    }
}
