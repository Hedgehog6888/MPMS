namespace MPMS.API.Models;

public class TaskDependency
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }
    public ProjectTask Task { get; set; } = null!;

    public Guid DependsOnTaskId { get; set; }
    public ProjectTask DependsOnTask { get; set; } = null!;

    public TaskDependencyType DependencyType { get; set; } = TaskDependencyType.FinishToStart;
}
