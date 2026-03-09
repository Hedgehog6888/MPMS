using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class ActivityLog
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public ActivityActionType ActionType { get; set; }

    public ActivityEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
