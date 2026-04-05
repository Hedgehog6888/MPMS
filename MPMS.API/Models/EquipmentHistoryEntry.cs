using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class EquipmentHistoryEntry
{
    public Guid Id { get; set; }

    public Guid EquipmentId { get; set; }
    public Equipment Equipment { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    public EquipmentHistoryEventType EventType { get; set; }

    public EquipmentStatus? PreviousStatus { get; set; }

    public EquipmentStatus? NewStatus { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? TaskId { get; set; }
    public ProjectTask? Task { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }
}
