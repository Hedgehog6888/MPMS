using MPMS.Models;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Services;

/// <summary>Единая формула прогресса: учитывает статусы, этапы, средний прогресс и просрочку.</summary>
public static class ProgressCalculator
{
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

    public static void ApplyTaskMetrics(LocalTask task, IReadOnlyCollection<LocalTaskStage> stages)
    {
        var activeStages = stages
            .Where(s => !s.IsArchived && !s.IsMarkedForDeletion)
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
            .Where(s => !s.IsArchived && !s.IsMarkedForDeletion && taskIds.Contains(s.TaskId))
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
            var rawWithoutStages = task.Status switch
            {
                TaskStatus.Completed => 100d,
                TaskStatus.InProgress => 55d,
                TaskStatus.Paused => 28d,
                _ => 8d
            };

            if (task.IsOverdue && task.Status != TaskStatus.Completed)
                rawWithoutStages = Math.Max(0, rawWithoutStages - 12);

            return (int)Math.Round(Math.Clamp(rawWithoutStages, 0, 100), MidpointRounding.AwayFromZero);
        }

        double total = task.TotalStages;
        double completionRatio = task.CompletedStages / total;
        double activeRatio = task.InProgressStages / total;
        double stageStatusScore = (
            task.CompletedStages * GetStageWeight(StageStatus.Completed) +
            task.InProgressStages * GetStageWeight(StageStatus.InProgress) +
            task.PlannedStages * GetStageWeight(StageStatus.Planned)) / total;
        double statusScore = GetTaskWeight(task.Status);

        double raw = (
            stageStatusScore * 0.55 +
            completionRatio * 0.25 +
            activeRatio * 0.10 +
            statusScore * 0.10) * 100;

        if (task.IsOverdue && task.Status != TaskStatus.Completed)
            raw -= 12;

        if (task.CompletedStages == 0 && task.InProgressStages == 0)
            raw = Math.Min(raw, 15);

        if (task.Status == TaskStatus.Completed && task.CompletedStages >= task.TotalStages)
            raw = 100;

        return (int)Math.Round(Math.Clamp(raw, 0, 100), MidpointRounding.AwayFromZero);
    }

    public static int GetProjectProgressPercent(LocalProject project)
    {
        if (project.TotalTasks <= 0)
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
        double stageStatusScore = averageTaskScore;

        if (project.TotalStages > 0)
        {
            double plannedStages = Math.Max(0, project.TotalStages - project.CompletedStages - project.InProgressStages);
            stageStatusScore = (
                project.CompletedStages * GetStageWeight(StageStatus.Completed) +
                project.InProgressStages * GetStageWeight(StageStatus.InProgress) +
                plannedStages * GetStageWeight(StageStatus.Planned)) / project.TotalStages;
        }

        double overduePenalty = project.OverdueTasks <= 0
            ? 0
            : Math.Min(0.18, (project.OverdueTasks / totalTasks) * 0.18);

        double raw = (
            taskStatusScore * 0.30 +
            averageTaskScore * 0.40 +
            stageStatusScore * 0.20 +
            completionScore * 0.10) * 100;

        raw -= overduePenalty * 100;

        if (project.CompletedTasks == project.TotalTasks)
            raw = 100;
        else if (project.CompletedTasks == 0 && project.InProgressTasks == 0 && project.InProgressStages == 0)
            raw = Math.Min(raw, 12);

        return (int)Math.Round(Math.Clamp(raw, 0, 100), MidpointRounding.AwayFromZero);
    }

    /// <summary>Строка для отображения: "67.5%" или "100%"</summary>
    public static string FormatPercent(double value)
    {
        return value % 1 == 0 ? $"{(int)value}%" : $"{value:F1}%";
    }
}
