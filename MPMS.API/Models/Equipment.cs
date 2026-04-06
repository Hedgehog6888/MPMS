using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class Equipment
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }
    public EquipmentCategory? Category { get; set; }

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public EquipmentStatus Status { get; set; } = EquipmentStatus.Available;
    public EquipmentCondition Condition { get; set; } = EquipmentCondition.Good;

    [MaxLength(100)]
    public string? InventoryNumber { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? CheckedOutProjectId { get; set; }
    public Project? CheckedOutProject { get; set; }

    public Guid? CheckedOutTaskId { get; set; }
    public ProjectTask? CheckedOutTask { get; set; }

    public bool IsWrittenOff { get; set; }

    public DateTime? WrittenOffAt { get; set; }

    [MaxLength(500)]
    public string? WrittenOffComment { get; set; }

    public ICollection<EquipmentHistoryEntry> History { get; set; } = new List<EquipmentHistoryEntry>();
}
