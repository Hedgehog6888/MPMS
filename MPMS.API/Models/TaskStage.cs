using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class TaskStage
{
    public Guid Id { get; set; }

    public Guid TaskId { get; set; }
    public ProjectTask Task { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public StageStatus Status { get; set; } = StageStatus.Planned;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<StageMaterial> StageMaterials { get; set; } = new List<StageMaterial>();
    public ICollection<FileAttachment> Files { get; set; } = new List<FileAttachment>();
}
