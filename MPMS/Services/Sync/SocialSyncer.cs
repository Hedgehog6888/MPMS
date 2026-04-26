using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using System.Text.Json;

namespace MPMS.Services.Sync;

public class SocialSyncer : IEntitySyncer
{
    private readonly IApiService _api;
    private readonly JsonSerializerOptions _jsonOptions;

    public SocialSyncer(IApiService api, JsonSerializerOptions jsonOptions)
    {
        _api = api;
        _jsonOptions = jsonOptions;
    }

    public bool CanHandle(string entityType) => 
        entityType is "DiscussionMessage" or "SyncedActivityLog";

    public Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    public async Task PullAsync(LocalDbContext db)
    {
        // 1. Discussion messages
        DateTime? msgSince = null;
        if (await db.Messages.AnyAsync())
            msgSince = await db.Messages.MaxAsync(m => m.CreatedAt);
        var remoteMsgs = await _api.GetDiscussionMessagesAsync(since: msgSince);
        if (remoteMsgs is not null)
        {
            foreach (var m in remoteMsgs)
            {
                if (await db.Messages.AnyAsync(x => x.Id == m.Id)) continue;
                db.Messages.Add(new LocalMessage
                {
                    Id = m.Id,
                    TaskId = m.TaskId,
                    ProjectId = m.ProjectId,
                    UserId = m.UserId,
                    UserName = m.UserName,
                    UserInitials = m.UserInitials,
                    UserColor = m.UserColor,
                    UserRole = m.UserRole,
                    Text = m.Text,
                    CreatedAt = NormalizeUtcInstant(m.CreatedAt)
                });
            }
        }

        // 2. Activity logs
        DateTime? actSince = null;
        if (await db.ActivityLogs.AnyAsync())
            actSince = await db.ActivityLogs.MaxAsync(a => a.CreatedAt);
        var remoteActs = await _api.GetSyncedActivityLogsAsync(since: actSince);
        if (remoteActs is not null)
        {
            foreach (var a in remoteActs)
            {
                if (await db.ActivityLogs.AnyAsync(x => x.Id == a.Id)) continue;
                db.ActivityLogs.Add(new LocalActivityLog
                {
                    Id = a.Id,
                    UserId = a.UserId,
                    ActorRole = a.ActorRole,
                    UserName = a.UserName,
                    UserInitials = a.UserInitials,
                    UserColor = a.UserColor,
                    ActionType = a.ActionType,
                    ActionText = a.ActionText,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    CreatedAt = NormalizeUtcInstant(a.CreatedAt)
                });
            }
        }
    }

    public async Task<bool> PushAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.EntityType == "DiscussionMessage") return await SyncDiscussionMessageAsync(db, op);
        if (op.EntityType == "SyncedActivityLog") return await SyncSyncedActivityLogAsync(db, op);
        return false;
    }

    private async Task<bool> SyncDiscussionMessageAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateDiscussionMessageRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        req = NormalizeDiscussionRequest(req);
        var response = await _api.PostDiscussionMessageAsync(req);
        if (response is null) return false;

        var messageId = req.Id ?? op.EntityId;
        var local = await db.Messages.FindAsync(messageId);
        if (local is not null)
        {
            local.CreatedAt = NormalizeUtcInstant(response.CreatedAt);
        }

        return true;
    }

    private async Task<bool> SyncSyncedActivityLogAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateSyncedActivityLogRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        return await _api.PostSyncedActivityLogAsync(req) is not null;
    }

    private static CreateDiscussionMessageRequest NormalizeDiscussionRequest(CreateDiscussionMessageRequest req)
    {
        var taskId = req.TaskId;
        var projectId = req.ProjectId;
        if (taskId == Guid.Empty) taskId = null;
        if (projectId == Guid.Empty) projectId = null;
        return req with { TaskId = taskId, ProjectId = projectId };
    }

    private static DateTime NormalizeUtcInstant(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
    };
}
