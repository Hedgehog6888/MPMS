using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

public interface ISyncService
{
    bool IsSyncing { get; }
    bool IsOnline  { get; }
    event EventHandler<bool>? OnlineStatusChanged;
    Task SyncAsync();
    Task QueueOperationAsync(string entityType, Guid entityId,
        SyncOperation operation, object payload);
}

public class SyncService : ISyncService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IApiService _api;
    private readonly IAuthService _auth;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(30));
    private bool _isSyncing;

    public bool IsSyncing => _isSyncing;
    public bool IsOnline  => _api.IsOnline;
    public event EventHandler<bool>? OnlineStatusChanged;

    public SyncService(IDbContextFactory<LocalDbContext> dbFactory,
        IApiService api, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _api = api;
        _auth = auth;
        _ = RunPeriodicSyncAsync();
    }

    public async Task SyncAsync()
    {
        if (_isSyncing || !_auth.IsAuthenticated) return;
        _isSyncing = true;

        try
        {
            await ProcessPendingOperationsAsync();
            await PullFromServerAsync();
        }
        catch { /* errors are captured via _api.IsOnline */ }
        finally
        {
            _isSyncing = false;
            // Always fire so the UI reflects the current connectivity state,
            // even when an exception interrupted the sync.
            OnlineStatusChanged?.Invoke(this, _api.IsOnline);
        }
    }

    public async Task QueueOperationAsync(string entityType, Guid entityId,
        SyncOperation operation, object payload)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.PendingOperations.Add(new PendingOperation
        {
            EntityType = entityType,
            EntityId = entityId,
            OperationType = operation,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Try immediate sync if online
        _ = SyncAsync();
    }

    // ── Pull latest data from server into local DB ────────────────────────────
    private async Task PullFromServerAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Users (for dropdowns)
        var users = await _api.GetUsersAsync();
        if (!_api.IsOnline) return;  // bail out early if the first request already failed
        if (users is not null)
        {
            var ids = users.Select(u => u.Id).ToHashSet();
            var existing = await db.Users.ToDictionaryAsync(u => u.Id);

            foreach (var u in users)
            {
                if (existing.TryGetValue(u.Id, out var local))
                {
                    local.Name = u.Name; local.Username = u.Username;
                    local.Email = u.Email;
                    local.RoleName = u.Role; local.IsSynced = true;
                }
                else
                {
                    db.Users.Add(new LocalUser
                    {
                        Id = u.Id, Name = u.Name, Username = u.Username,
                        Email = u.Email, RoleName = u.Role,
                        IsSynced = true, CreatedAt = u.CreatedAt
                    });
                }
            }
        }

        // Projects
        var projects = await _api.GetProjectsAsync();
        if (projects is not null)
        {
            var existingProjects = await db.Projects.ToDictionaryAsync(p => p.Id);
            foreach (var p in projects)
            {
                if (existingProjects.TryGetValue(p.Id, out var local))
                {
                    local.Name = p.Name; local.Client = p.Client;
                    local.StartDate = p.StartDate; local.EndDate = p.EndDate;
                    local.Status = Enum.Parse<ProjectStatus>(p.Status);
                    local.ManagerName = p.ManagerName; local.IsSynced = true;
                }
                else
                {
                    db.Projects.Add(new LocalProject
                    {
                        Id = p.Id, Name = p.Name, Client = p.Client,
                        StartDate = p.StartDate, EndDate = p.EndDate,
                        Status = Enum.Parse<ProjectStatus>(p.Status),
                        ManagerName = p.ManagerName,
                        IsSynced = true
                    });
                }
            }
        }

        // Tasks (all)
        var tasks = await _api.GetTasksAsync();
        if (tasks is not null)
        {
            var existingTasks = await db.Tasks.ToDictionaryAsync(t => t.Id);
            foreach (var t in tasks)
            {
                if (existingTasks.TryGetValue(t.Id, out var local))
                {
                    local.Name = t.Name; local.Status = Enum.Parse<Models.TaskStatus>(t.Status);
                    local.Priority = Enum.Parse<TaskPriority>(t.Priority);
                    local.AssignedUserName = t.AssignedUserName;
                    local.TotalStages = t.TotalStages;
                    local.CompletedStages = t.CompletedStages;
                    local.DueDate = t.DueDate; local.IsSynced = true;
                }
                else
                {
                    db.Tasks.Add(new LocalTask
                    {
                        Id = t.Id, ProjectId = t.ProjectId, ProjectName = t.ProjectName,
                        Name = t.Name,
                        AssignedUserName = t.AssignedUserName,
                        Priority = Enum.Parse<TaskPriority>(t.Priority),
                        DueDate = t.DueDate,
                        Status = Enum.Parse<Models.TaskStatus>(t.Status),
                        TotalStages = t.TotalStages, CompletedStages = t.CompletedStages,
                        IsSynced = true
                    });
                }
            }
        }

        // Materials
        var materials = await _api.GetMaterialsAsync();
        if (materials is not null)
        {
            var existingMats = await db.Materials.ToDictionaryAsync(m => m.Id);
            foreach (var m in materials)
            {
                if (existingMats.TryGetValue(m.Id, out var local))
                {
                    local.Name = m.Name; local.Unit = m.Unit;
                    local.Description = m.Description; local.IsSynced = true;
                }
                else
                {
                    db.Materials.Add(new LocalMaterial
                    {
                        Id = m.Id, Name = m.Name, Unit = m.Unit,
                        Description = m.Description, CreatedAt = m.CreatedAt, IsSynced = true
                    });
                }
            }
        }

        // Task Stages (pulled per task for all synced tasks)
        var taskIds = await db.Tasks.Where(t => t.IsSynced).Select(t => t.Id).ToListAsync();
        var existingStages = await db.TaskStages.ToDictionaryAsync(s => s.Id);
        foreach (var taskId in taskIds)
        {
            var task = await _api.GetTaskAsync(taskId);
            if (task?.Stages is null) continue;
            foreach (var s in task.Stages)
            {
                if (existingStages.TryGetValue(s.Id, out var localStage))
                {
                    localStage.Name = s.Name; localStage.Description = s.Description;
                    localStage.AssignedUserName = s.AssignedUserName;
                    localStage.Status = Enum.Parse<StageStatus>(s.Status);
                    localStage.IsSynced = true;
                }
                else
                {
                    db.TaskStages.Add(new LocalTaskStage
                    {
                        Id = s.Id, TaskId = s.TaskId, Name = s.Name,
                        Description = s.Description, AssignedUserName = s.AssignedUserName,
                        AssignedUserId = s.AssignedUserId,
                        Status = Enum.Parse<StageStatus>(s.Status),
                        IsSynced = true, CreatedAt = s.CreatedAt
                    });
                }

                // Sync stage materials
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
                            Unit = sm.Unit, Quantity = sm.Quantity, IsSynced = true
                        });
                    }
                    else
                    {
                        existingMat.Quantity = sm.Quantity;
                        existingMat.IsSynced = true;
                    }
                }
            }
        }

        await db.SaveChangesAsync();
    }

    // ── Send pending operations to server ─────────────────────────────────────
    private async Task ProcessPendingOperationsAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var pending = await db.PendingOperations
            .Where(p => !p.IsFailed)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();

        foreach (var op in pending)
        {
            var success = await ProcessOperationAsync(op);
            if (success)
                db.PendingOperations.Remove(op);
            else
            {
                op.RetryCount++;
                if (op.RetryCount >= 5) op.IsFailed = true;
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<bool> ProcessOperationAsync(PendingOperation op)
    {
        try
        {
            return op.EntityType switch
            {
                "Project"  => await SyncProjectAsync(op),
                "Task"     => await SyncTaskAsync(op),
                "Stage"    => await SyncStageAsync(op),
                "Material" => await SyncMaterialAsync(op),
                _          => true
            };
        }
        catch { return false; }
    }

    private async Task<bool> SyncProjectAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteProjectAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateProjectRequest>(op.Payload);
            if (req is null) return false;
            // Include the local ID so server creates with the same GUID
            req = req with { Id = op.EntityId };
            return await _api.CreateProjectAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateProjectRequest>(op.Payload);
        return updateReq is not null && await _api.UpdateProjectAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncTaskAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteTaskAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateTaskRequest>(op.Payload);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateTaskAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateTaskRequest>(op.Payload);
        return updateReq is not null && await _api.UpdateTaskAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncStageAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteStageAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateStageRequest>(op.Payload);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateStageAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateStageRequest>(op.Payload);
        return updateReq is not null && await _api.UpdateStageAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncMaterialAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteMaterialAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateMaterialAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload);
        return updateReq is not null && await _api.UpdateMaterialAsync(op.EntityId, updateReq) is not null;
    }

    private async Task RunPeriodicSyncAsync()
    {
        while (await _timer.WaitForNextTickAsync())
            await SyncAsync();
    }
}
