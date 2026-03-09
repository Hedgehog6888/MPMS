using System.ComponentModel.DataAnnotations;

namespace MPMS.Models;

/// <summary>
/// Offline sync queue. Every local change that hasn't reached the server
/// gets a record here, and SyncService processes them when online.
/// </summary>
public class PendingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(50)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public SyncOperation OperationType { get; set; }

    /// <summary>JSON payload — serialised request body to send to API</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; } = 0;
    public bool IsFailed { get; set; } = false;
    [MaxLength(500)] public string? ErrorMessage { get; set; }
}
