using MPMS.API.Models;

namespace MPMS.API.Services;

public interface IActivityLogService
{
    Task LogAsync(Guid userId, ActivityActionType action, ActivityEntityType entityType,
        Guid entityId, string? description = null);
}
