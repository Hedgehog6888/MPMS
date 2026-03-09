using MPMS.API.Data;
using MPMS.API.Models;

namespace MPMS.API.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly ApplicationDbContext _db;

    public ActivityLogService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(Guid userId, ActivityActionType action, ActivityEntityType entityType,
        Guid entityId, string? description = null)
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            UserId = userId,
            ActionType = action,
            EntityType = entityType,
            EntityId = entityId,
            Description = description,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
