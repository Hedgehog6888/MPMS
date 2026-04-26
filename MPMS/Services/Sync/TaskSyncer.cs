using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using System.Text.Json;

namespace MPMS.Services.Sync;

public class TaskSyncer : IEntitySyncer
{
    private readonly IApiService _api;
    private readonly JsonSerializerOptions _jsonOptions;

    public TaskSyncer(IApiService api, JsonSerializerOptions jsonOptions)
    {
        _api = api;
        _jsonOptions = jsonOptions;
    }

    public bool CanHandle(string entityType) => 
        entityType is "Task" or "Stage" or "TaskAssignees" or "StageAssignees";

    public Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    public async Task PullAsync(LocalDbContext db)
    {
        // 1. Pull basic task info
        var tasks = await _api.GetTasksAsync();
        if (tasks is not null)
        {
            var existingTasks = await db.Tasks.ToDictionaryAsync(t => t.Id);
            foreach (var t in tasks)
            {
                if (existingTasks.TryGetValue(t.Id, out var local))
                {
                    if (local.IsSynced)
                    {
                        local.Name = t.Name;
                        local.Status = Enum.Parse<Models.TaskStatus>(t.Status);
                        local.Priority = Enum.Parse<TaskPriority>(t.Priority);
                        local.AssignedUserId = t.AssignedUserId;
                        local.AssignedUserName = t.AssignedUserName;
                        local.TotalStages = t.TotalStages;
                        local.CompletedStages = t.CompletedStages;
                        local.DueDate = t.DueDate;
                        local.IsMarkedForDeletion = t.IsMarkedForDeletion;
                        local.IsArchived = t.IsArchived;
                        local.ProjectName = t.ProjectName;
                        local.IsSynced = true;
                    }
                    else
                    {
                        local.Name = t.Name;
                        local.Status = Enum.Parse<Models.TaskStatus>(t.Status);
                        local.Priority = Enum.Parse<TaskPriority>(t.Priority);
                        local.AssignedUserId = t.AssignedUserId;
                        local.AssignedUserName = t.AssignedUserName;
                        local.TotalStages = t.TotalStages;
                        local.CompletedStages = t.CompletedStages;
                        local.DueDate = t.DueDate;
                        local.IsMarkedForDeletion = t.IsMarkedForDeletion;
                        local.IsArchived = t.IsArchived;
                        local.ProjectName = t.ProjectName;
                    }
                }
                else
                {
                    db.Tasks.Add(new LocalTask
                    {
                        Id = t.Id, ProjectId = t.ProjectId, ProjectName = t.ProjectName,
                        Name = t.Name,
                        AssignedUserId = t.AssignedUserId,
                        AssignedUserName = t.AssignedUserName,
                        Priority = Enum.Parse<TaskPriority>(t.Priority),
                        DueDate = t.DueDate,
                        Status = Enum.Parse<Models.TaskStatus>(t.Status),
                        TotalStages = t.TotalStages, CompletedStages = t.CompletedStages,
                        IsMarkedForDeletion = t.IsMarkedForDeletion,
                        IsArchived = t.IsArchived,
                        IsSynced = true
                    });
                }
            }

            var serverTaskIds = tasks.Select(t => t.Id).ToHashSet();
            var orphanTaskIds = await db.Tasks
                .Where(t => t.IsSynced && !serverTaskIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync();
            foreach (var tid in orphanTaskIds)
                await LocalDbGraphDeletion.PermanentlyDeleteTaskGraphAsync(db, tid);
        }

        // 2. Pull detailed stage info and assignees
        var taskIds = await db.Tasks.Select(t => t.Id).ToListAsync();
        var existingStages = await db.TaskStages.ToDictionaryAsync(s => s.Id);
        foreach (var taskId in taskIds)
        {
            var taskApi = await _api.GetTaskAsync(taskId);
            if (taskApi?.Stages is null) continue;

            var localTaskRow = await db.Tasks.FindAsync(taskId);
            if (localTaskRow is not null)
            {
                localTaskRow.Description = taskApi.Description;
                localTaskRow.AssignedUserId = taskApi.AssignedUserId;
                localTaskRow.AssignedUserName = taskApi.AssignedUserName;
                localTaskRow.CreatedAt = taskApi.CreatedAt;
                localTaskRow.UpdatedAt = taskApi.UpdatedAt;
                localTaskRow.IsMarkedForDeletion = taskApi.IsMarkedForDeletion;
                localTaskRow.IsArchived = taskApi.IsArchived;
            }

            await db.TaskAssignees.Where(a => a.TaskId == taskId).ExecuteDeleteAsync();
            if (taskApi.AssigneeUserIds is { Count: > 0 })
            {
                foreach (var uid in taskApi.AssigneeUserIds)
                {
                    var uname = await db.Users.Where(u => u.Id == uid).Select(u => u.Name)
                        .FirstOrDefaultAsync() ?? "";
                    db.TaskAssignees.Add(new LocalTaskAssignee
                    {
                        Id = Guid.NewGuid(),
                        TaskId = taskId,
                        UserId = uid,
                        UserName = uname
                    });
                }
            }

            foreach (var s in taskApi.Stages)
            {
                if (existingStages.TryGetValue(s.Id, out var localStage))
                {
                    localStage.Name = s.Name;
                    localStage.Description = s.Description;
                    localStage.ServiceTemplateId = s.ServiceTemplateId;
                    localStage.ServiceNameSnapshot = s.ServiceName;
                    localStage.ServiceDescriptionSnapshot = s.ServiceDescription;
                    localStage.WorkUnitSnapshot = s.WorkUnit;
                    localStage.WorkQuantity = s.WorkQuantity;
                    localStage.WorkPricePerUnit = s.WorkPricePerUnit;
                    localStage.AssignedUserName = s.AssignedUserName;
                    localStage.AssignedUserId = s.AssignedUserId;
                    localStage.DueDate = s.DueDate;
                    localStage.Status = Enum.Parse<StageStatus>(s.Status);
                    localStage.UpdatedAt = s.UpdatedAt;
                    localStage.IsMarkedForDeletion = s.IsMarkedForDeletion;
                    localStage.IsArchived = s.IsArchived;
                }
                else
                {
                    db.TaskStages.Add(new LocalTaskStage
                    {
                        Id = s.Id, TaskId = s.TaskId, Name = s.Name,
                        Description = s.Description, AssignedUserName = s.AssignedUserName,
                        ServiceTemplateId = s.ServiceTemplateId,
                        ServiceNameSnapshot = s.ServiceName,
                        ServiceDescriptionSnapshot = s.ServiceDescription,
                        WorkUnitSnapshot = s.WorkUnit,
                        WorkQuantity = s.WorkQuantity,
                        WorkPricePerUnit = s.WorkPricePerUnit,
                        AssignedUserId = s.AssignedUserId,
                        DueDate = s.DueDate,
                        Status = Enum.Parse<StageStatus>(s.Status),
                        IsMarkedForDeletion = s.IsMarkedForDeletion,
                        IsArchived = s.IsArchived,
                        IsSynced = true,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt
                    });
                }

                await db.StageAssignees.Where(a => a.StageId == s.Id).ExecuteDeleteAsync();
                await db.StageServices.Where(x => x.StageId == s.Id).ExecuteDeleteAsync();
                
                if (s.AssigneeUserIds is { Count: > 0 })
                {
                    foreach (var uid in s.AssigneeUserIds)
                    {
                        var uname = await db.Users.Where(u => u.Id == uid).Select(u => u.Name)
                            .FirstOrDefaultAsync() ?? "";
                        db.StageAssignees.Add(new LocalStageAssignee
                        {
                            Id = Guid.NewGuid(),
                            StageId = s.Id,
                            UserId = uid,
                            UserName = uname
                        });
                    }
                }

                if (s.Services is { Count: > 0 })
                {
                    foreach (var ss in s.Services)
                    {
                        db.StageServices.Add(new LocalStageService
                        {
                            Id = ss.Id,
                            StageId = s.Id,
                            ServiceTemplateId = ss.ServiceTemplateId,
                            ServiceName = ss.ServiceName,
                            ServiceDescription = ss.ServiceDescription,
                            Unit = ss.Unit,
                            Quantity = ss.Quantity,
                            PricePerUnit = ss.PricePerUnit,
                            IsSynced = true
                        });
                    }
                }

                foreach (var sm in s.Materials)
                {
                    var existingMat = await db.StageMaterials
                        .FirstOrDefaultAsync(x => x.Id == sm.Id);
                    if (existingMat is null)
                    {
                        db.StageMaterials.Add(new LocalStageMaterial
                        {
                            Id = sm.Id, StageId = s.Id,
                            MaterialId = sm.MaterialId, MaterialName = sm.MaterialName,
                            Unit = sm.Unit, Quantity = sm.Quantity, PricePerUnit = sm.PricePerUnit, IsSynced = true
                        });
                    }
                    else
                    {
                        existingMat.Quantity = sm.Quantity;
                        existingMat.PricePerUnit = sm.PricePerUnit;
                        existingMat.IsSynced = true;
                    }
                }
            }
        }
    }

    public async Task<bool> PushAsync(LocalDbContext db, PendingOperation op)
    {
        return op.EntityType switch
        {
            "Task" => await SyncTaskAsync(db, op),
            "Stage" => await SyncStageAsync(db, op),
            "TaskAssignees" => await SyncTaskAssigneesAsync(db, op),
            "StageAssignees" => await SyncStageAssigneesAsync(db, op),
            _ => false
        };
    }

    private async Task<bool> SyncTaskAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteTaskAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateTaskRequest>(op.Payload, _jsonOptions);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateTaskAsync(req);
            if (created is null) return false;
            
            var local = await db.Tasks.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateTaskRequest>(op.Payload, _jsonOptions);
        if (updateReq is null) return false;
        var updated = await _api.UpdateTaskAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.Tasks.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }

    private async Task<bool> SyncStageAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteStageAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateStageRequest>(op.Payload, _jsonOptions);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateStageAsync(req);
            if (created is null) return false;
            
            var local = await db.TaskStages.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateStageRequest>(op.Payload, _jsonOptions);
        if (updateReq is null) return false;
        var updated = await _api.UpdateStageAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.TaskStages.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }

    private async Task<bool> SyncTaskAssigneesAsync(LocalDbContext db, PendingOperation op)
    {
        var req = JsonSerializer.Deserialize<ReplaceTaskAssigneesRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        return await _api.ReplaceTaskAssigneesAsync(op.EntityId, req);
    }

    private async Task<bool> SyncStageAssigneesAsync(LocalDbContext db, PendingOperation op)
    {
        var req = JsonSerializer.Deserialize<ReplaceStageAssigneesRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        return await _api.ReplaceStageAssigneesAsync(op.EntityId, req);
    }
}
