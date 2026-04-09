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

    public Guid? ServiceTemplateId { get; set; }
    public ServiceTemplate? ServiceTemplate { get; set; }

    [MaxLength(200)]
    public string? ServiceNameSnapshot { get; set; }

    public string? ServiceDescriptionSnapshot { get; set; }

    [MaxLength(50)]
    public string? WorkUnitSnapshot { get; set; }

    public decimal WorkQuantity { get; set; }
    public decimal WorkPricePerUnit { get; set; }

    public Guid? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    public StageStatus Status { get; set; } = StageStatus.Planned;

    public DateOnly? DueDate { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsMarkedForDeletion { get; set; }

    public bool IsArchived { get; set; }

    public ICollection<StageMaterial> StageMaterials { get; set; } = new List<StageMaterial>();
    public ICollection<StageService> StageServices { get; set; } = new List<StageService>();
    public ICollection<FileAttachment> Files { get; set; } = new List<FileAttachment>();

    public ICollection<StageAssignee> StageAssignees { get; set; } = new List<StageAssignee>();
}
