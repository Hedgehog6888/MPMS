using System.ComponentModel.DataAnnotations;

namespace MPMS.Models;

/// <summary>
/// Очередь офлайн синхронизации. Каждое локальное изменение, которое не дошло до сервера,
/// получает запись здесь, и SyncService обрабатывает их при онлайн.
/// </summary>
public class PendingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(50)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    public SyncOperation OperationType { get; set; }

    /// <summary>JSON нагрузка — сериализованное тело запроса для отправки в API</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; } = 0;
    public bool IsFailed { get; set; } = false;
    [MaxLength(500)] public string? ErrorMessage { get; set; }
}
