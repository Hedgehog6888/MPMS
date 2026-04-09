using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

/// <summary>
/// Named ProjectTask to avoid conflict with System.Threading.Tasks.Task
/// </summary>
public class ProjectTask
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public DateOnly? DueDate { get; set; }

    public Models.TaskStatus Status { get; set; } = Models.TaskStatus.Planned;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsMarkedForDeletion { get; set; }

    public bool IsArchived { get; set; }

    public ICollection<TaskStage> Stages { get; set; } = new List<TaskStage>();
    public ICollection<FileAttachment> Files { get; set; } = new List<FileAttachment>();

    public ICollection<TaskDependency> Dependencies { get; set; } = new List<TaskDependency>();
    public ICollection<TaskDependency> Dependents { get; set; } = new List<TaskDependency>();

    public ICollection<TaskAssignee> TaskAssignees { get; set; } = new List<TaskAssignee>();
}
