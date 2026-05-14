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

    public Guid? WorkTypeTemplateId { get; set; }
    public WorkTypeTemplate? WorkTypeTemplate { get; set; }

    [MaxLength(200)]
    public string? WorkTypeNameSnapshot { get; set; }

    public string? WorkTypeDescriptionSnapshot { get; set; }

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
    public ICollection<StageWorkType> StageWorkTypes { get; set; } = new List<StageWorkType>();
    public ICollection<FileAttachment> Files { get; set; } = new List<FileAttachment>();

    public ICollection<StageAssignee> StageAssignees { get; set; } = new List<StageAssignee>();
}
