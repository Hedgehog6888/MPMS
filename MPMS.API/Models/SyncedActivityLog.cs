using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

/// <summary>Лента активности с клиента (типы действий шире, чем у серверного ActivityLog).</summary>
public class SyncedActivityLog
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(50)] public string? ActorRole { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)] public string UserInitials { get; set; } = "?";
    [MaxLength(20)] public string UserColor { get; set; } = "#1B6EC2";
    [MaxLength(50)] public string? ActionType { get; set; }
    [MaxLength(500)] public string ActionText { get; set; } = string.Empty;
    [MaxLength(50)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; }
}
