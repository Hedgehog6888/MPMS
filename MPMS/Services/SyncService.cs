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

    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(5));
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
            // Сначала проверяем соединение — иначе при IsOnline=false запросы не отправляются
            await _api.ProbeAsync();
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

        // Roles (for admin form)
        var apiRoles = await _api.GetRolesAsync();
        if (apiRoles is not null)
        {
            foreach (var r in apiRoles)
            {
                var existing = await db.Roles.FindAsync(r.Id);
                if (existing is null)
                    db.Roles.Add(new LocalRole { Id = r.Id, Name = r.Name, Description = r.Description });
                else
                    existing.Name = r.Name;
            }
        }

        // Users (for dropdowns)
        var users = await _api.GetUsersAsync();
        if (!_api.IsOnline) return;  // bail out early if the first request already failed
        if (users is not null)
        {
            var deletedIds = (await db.DeletedUserIds.Select(x => x.Id).ToListAsync()).ToHashSet();
            var existing = await db.Users.ToDictionaryAsync(u => u.Id);

            foreach (var u in users)
            {
                if (deletedIds.Contains(u.Id)) continue; // Не возвращать локально удалённых
                var fullName = $"{u.FirstName} {u.LastName}".Trim();
                if (existing.TryGetValue(u.Id, out var local))
                {
                    if (local.IsSynced)
                    {
                        local.Name = fullName;
                        local.FirstName = u.FirstName; local.LastName = u.LastName;
                        local.Username = u.Username;
                        local.Email = u.Email;
                        local.RoleName = u.Role;
                        local.RoleId = u.RoleId;
                        local.SubRole = u.SubRole;
                        local.AdditionalSubRoles = u.AdditionalSubRoles;
                        local.BirthDate = u.BirthDate;
                        local.HomeAddress = u.HomeAddress;
                        if (u.AvatarData is { Length: > 0 })
                            local.AvatarData = u.AvatarData;
                        local.IsSynced = true;
                    }
                    else
                    {
                        // Локальные правки профиля ещё не на сервере — не затираем ФИО, дату, адрес и аватар
                        local.RoleName = u.Role;
                        local.RoleId = u.RoleId;
                        local.SubRole = u.SubRole;
                        local.AdditionalSubRoles = u.AdditionalSubRoles;
                    }
                }
                else
                {
                    db.Users.Add(new LocalUser
                    {
                        Id = u.Id, Name = fullName,
                        FirstName = u.FirstName, LastName = u.LastName,
                        Username = u.Username, Email = u.Email, RoleName = u.Role,
                        RoleId = u.RoleId,
                        SubRole = u.SubRole,
                        AdditionalSubRoles = u.AdditionalSubRoles,
                        BirthDate = u.BirthDate,
                        HomeAddress = u.HomeAddress,
                        AvatarData = u.AvatarData,
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

        // Material categories
        var matCats = await _api.GetMaterialCategoriesAsync();
        if (matCats is not null)
        {
            foreach (var c in matCats)
            {
                var ex = await db.MaterialCategories.FindAsync(c.Id);
                if (ex is null)
                    db.MaterialCategories.Add(new LocalMaterialCategory { Id = c.Id, Name = c.Name });
                else
                    ex.Name = c.Name;
            }
        }

        // Equipment categories
        var eqCats = await _api.GetEquipmentCategoriesAsync();
        if (eqCats is not null)
        {
            foreach (var c in eqCats)
            {
                var ex = await db.EquipmentCategories.FindAsync(c.Id);
                if (ex is null)
                    db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = c.Id, Name = c.Name });
                else
                    ex.Name = c.Name;
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
                    local.Name = m.Name;
                    local.Unit = m.Unit;
                    local.Description = m.Description;
                    local.Quantity = m.Quantity;
                    local.Cost = m.Cost;
                    local.InventoryNumber = m.InventoryNumber;
                    local.CategoryId = m.CategoryId;
                    local.CategoryName = m.CategoryName;
                    local.ImagePath = m.ImagePath;
                    local.UpdatedAt = m.UpdatedAt;
                    local.IsSynced = true;
                }
                else
                {
                    db.Materials.Add(new LocalMaterial
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Unit = m.Unit,
                        Description = m.Description,
                        Quantity = m.Quantity,
                        Cost = m.Cost,
                        InventoryNumber = m.InventoryNumber,
                        CategoryId = m.CategoryId,
                        CategoryName = m.CategoryName,
                        ImagePath = m.ImagePath,
                        CreatedAt = m.CreatedAt,
                        UpdatedAt = m.UpdatedAt,
                        IsSynced = true
                    });
                }
            }
        }

        // Material stock movements (полная замена снимка с сервера)
        var stockMoves = await _api.GetAllMaterialStockMovementsAsync();
        if (stockMoves is not null && _api.IsOnline)
        {
            await db.MaterialStockMovements.ExecuteDeleteAsync();
            foreach (var x in stockMoves)
            {
                db.MaterialStockMovements.Add(new LocalMaterialStockMovement
                {
                    Id = x.Id,
                    MaterialId = x.MaterialId,
                    OccurredAt = x.OccurredAt,
                    Delta = x.Delta,
                    QuantityAfter = x.QuantityAfter,
                    OperationType = x.OperationType,
                    Comment = x.Comment,
                    UserId = x.UserId,
                    ProjectId = x.ProjectId,
                    TaskId = x.TaskId
                });
            }
        }

        // Equipment
        var equips = await _api.GetAllEquipmentAsync();
        if (equips is not null && _api.IsOnline)
        {
            foreach (var eq in equips)
            {
                var existingEq = await db.Equipments.FindAsync(eq.Id);
                if (existingEq is null)
                {
                    db.Equipments.Add(new LocalEquipment
                    {
                        Id = eq.Id,
                        Name = eq.Name,
                        Description = eq.Description,
                        CategoryId = eq.CategoryId,
                        CategoryName = eq.CategoryName,
                        ImagePath = eq.ImagePath,
                        Status = eq.Status,
                        Condition = eq.Condition,
                        InventoryNumber = eq.InventoryNumber,
                        CreatedAt = eq.CreatedAt,
                        UpdatedAt = eq.UpdatedAt,
                        CheckedOutProjectId = eq.CheckedOutProjectId,
                        CheckedOutTaskId = eq.CheckedOutTaskId,
                        IsSynced = true
                    });
                }
                else
                {
                    existingEq.Name = eq.Name;
                    existingEq.Description = eq.Description;
                    existingEq.CategoryId = eq.CategoryId;
                    existingEq.CategoryName = eq.CategoryName;
                    existingEq.ImagePath = eq.ImagePath;
                    existingEq.Status = eq.Status;
                    existingEq.Condition = eq.Condition;
                    existingEq.InventoryNumber = eq.InventoryNumber;
                    existingEq.UpdatedAt = eq.UpdatedAt;
                    existingEq.CheckedOutProjectId = eq.CheckedOutProjectId;
                    existingEq.CheckedOutTaskId = eq.CheckedOutTaskId;
                    existingEq.IsSynced = true;
                }
            }
        }

        // История оборудования
        var eqHistory = await _api.GetAllEquipmentHistoryAsync();
        if (eqHistory is not null && _api.IsOnline)
        {
            await db.EquipmentHistoryEntries.ExecuteDeleteAsync();
            foreach (var h in eqHistory)
            {
                db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
                {
                    Id = h.Id,
                    EquipmentId = h.EquipmentId,
                    OccurredAt = h.OccurredAt,
                    EventType = h.EventType,
                    PreviousStatus = h.PreviousStatus,
                    NewStatus = h.NewStatus,
                    ProjectId = h.ProjectId,
                    TaskId = h.TaskId,
                    UserId = h.UserId,
                    Comment = h.Comment
                });
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
                    localStage.AssignedUserId = s.AssignedUserId;
                    localStage.DueDate = s.DueDate;
                    localStage.Status = Enum.Parse<StageStatus>(s.Status);
                    localStage.UpdatedAt = s.UpdatedAt;
                    localStage.IsSynced = true;
                }
                else
                {
                    db.TaskStages.Add(new LocalTaskStage
                    {
                        Id = s.Id, TaskId = s.TaskId, Name = s.Name,
                        Description = s.Description, AssignedUserName = s.AssignedUserName,
                        AssignedUserId = s.AssignedUserId,
                        DueDate = s.DueDate,
                        Status = Enum.Parse<StageStatus>(s.Status),
                        IsSynced = true,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt
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
        await EnsureWarehouseCategoriesReadyAsync(db);
        await RecoverWarehouseFailedOperationsAsync(db);

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

    private static async Task RecoverWarehouseFailedOperationsAsync(LocalDbContext db)
    {
        var recoverableTypes = new[]
        {
            "Material", "Equipment",
            "MaterialCategory", "EquipmentCategory",
            "MaterialStockMovement", "EquipmentHistory"
        };

        var failed = await db.PendingOperations
            .Where(p => p.IsFailed && recoverableTypes.Contains(p.EntityType))
            .ToListAsync();

        foreach (var op in failed)
        {
            op.IsFailed = false;
            op.RetryCount = 0;
            op.ErrorMessage = null;
        }

        if (failed.Count > 0)
            await db.SaveChangesAsync();
    }

    private async Task EnsureWarehouseCategoriesReadyAsync(LocalDbContext db)
    {
        var apiMatCats = await _api.GetMaterialCategoriesAsync() ?? [];
        var apiEqCats = await _api.GetEquipmentCategoriesAsync() ?? [];

        var localMatCats = await db.MaterialCategories.ToListAsync();
        foreach (var local in localMatCats)
        {
            if (apiMatCats.Any(c => c.Id == local.Id))
                continue;

            var byName = apiMatCats.FirstOrDefault(c =>
                string.Equals(c.Name, local.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                await RemapMaterialCategoryAsync(db, local.Id, byName.Id, byName.Name);
                continue;
            }

            var queued = await db.PendingOperations.AnyAsync(p =>
                !p.IsFailed &&
                p.EntityType == "MaterialCategory" &&
                p.OperationType == SyncOperation.Create &&
                p.EntityId == local.Id);
            if (!queued)
            {
                db.PendingOperations.Add(new PendingOperation
                {
                    EntityType = "MaterialCategory",
                    EntityId = local.Id,
                    OperationType = SyncOperation.Create,
                    Payload = JsonSerializer.Serialize(new CreateMaterialCategoryRequest(local.Name, local.Id)),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        var localEqCats = await db.EquipmentCategories.ToListAsync();
        foreach (var local in localEqCats)
        {
            if (apiEqCats.Any(c => c.Id == local.Id))
                continue;

            var byName = apiEqCats.FirstOrDefault(c =>
                string.Equals(c.Name, local.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                await RemapEquipmentCategoryAsync(db, local.Id, byName.Id, byName.Name);
                continue;
            }

            var queued = await db.PendingOperations.AnyAsync(p =>
                !p.IsFailed &&
                p.EntityType == "EquipmentCategory" &&
                p.OperationType == SyncOperation.Create &&
                p.EntityId == local.Id);
            if (!queued)
            {
                db.PendingOperations.Add(new PendingOperation
                {
                    EntityType = "EquipmentCategory",
                    EntityId = local.Id,
                    OperationType = SyncOperation.Create,
                    Payload = JsonSerializer.Serialize(new CreateEquipmentCategoryRequest(local.Name, local.Id)),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task RemapMaterialCategoryAsync(LocalDbContext db, Guid oldId, Guid newId, string newName)
    {
        if (oldId == newId) return;

        var targetCat = await db.MaterialCategories.FindAsync(newId);
        if (targetCat is null)
            db.MaterialCategories.Add(new LocalMaterialCategory { Id = newId, Name = newName });
        else
            targetCat.Name = newName;

        var mats = await db.Materials.Where(m => m.CategoryId == oldId).ToListAsync();
        foreach (var m in mats)
        {
            m.CategoryId = newId;
            m.CategoryName = newName;
            m.IsSynced = false;
        }

        var matOps = await db.PendingOperations
            .Where(p => !p.IsFailed && p.EntityType == "Material")
            .ToListAsync();
        foreach (var op in matOps)
        {
            if (op.OperationType == SyncOperation.Create)
            {
                var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId });
            }
            else if (op.OperationType == SyncOperation.Update)
            {
                var req = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId });
            }
        }

        var oldCat = await db.MaterialCategories.FindAsync(oldId);
        if (oldCat is not null)
            db.MaterialCategories.Remove(oldCat);
    }

    private async Task RemapEquipmentCategoryAsync(LocalDbContext db, Guid oldId, Guid newId, string newName)
    {
        if (oldId == newId) return;

        var targetCat = await db.EquipmentCategories.FindAsync(newId);
        if (targetCat is null)
            db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = newId, Name = newName });
        else
            targetCat.Name = newName;

        var equips = await db.Equipments.Where(e => e.CategoryId == oldId).ToListAsync();
        foreach (var e in equips)
        {
            e.CategoryId = newId;
            e.CategoryName = newName;
            e.IsSynced = false;
        }

        var eqOps = await db.PendingOperations
            .Where(p => !p.IsFailed && p.EntityType == "Equipment")
            .ToListAsync();
        foreach (var op in eqOps)
        {
            if (op.OperationType == SyncOperation.Create)
            {
                var req = JsonSerializer.Deserialize<CreateEquipmentRequest>(op.Payload);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId });
            }
            else if (op.OperationType == SyncOperation.Update)
            {
                var req = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId });
            }
        }

        var oldCat = await db.EquipmentCategories.FindAsync(oldId);
        if (oldCat is not null)
            db.EquipmentCategories.Remove(oldCat);
    }

    private async Task<bool> ProcessOperationAsync(PendingOperation op)
    {
        try
        {
            return op.EntityType switch
            {
                "Project"     => await SyncProjectAsync(op),
                "Task"        => await SyncTaskAsync(op),
                "Stage"       => await SyncStageAsync(op),
                "Material"    => await SyncMaterialAsync(op),
                "MaterialCategory" => await SyncMaterialCategoryAsync(op),
                "EquipmentCategory" => await SyncEquipmentCategoryAsync(op),
                "MaterialStockMovement" => await SyncMaterialStockMovementAsync(op),
                "Equipment" => await SyncEquipmentAsync(op),
                "EquipmentHistory" => await SyncEquipmentHistoryAsync(op),
                "User"        => await SyncUserAvatarAsync(op),
                "UserProfile" => await SyncUserProfileAsync(op),
                _             => true
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

    private async Task<bool> SyncUserAvatarAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var payload = JsonSerializer.Deserialize<UploadAvatarRequest>(op.Payload);
        if (payload?.AvatarData is null || payload.AvatarData.Length == 0) return true;
        var ok = await _api.UploadUserAvatarAsync(op.EntityId, payload.AvatarData);
        if (ok)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var user = await db.Users.FindAsync(op.EntityId);
            if (user is not null) { user.IsSynced = true; await db.SaveChangesAsync(); }
        }
        return ok;
    }

    private async Task<bool> SyncUserProfileAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var req = JsonSerializer.Deserialize<UpdateUserRequest>(op.Payload);
        if (req is null) return false;
        var updated = await _api.UpdateUserAsync(op.EntityId, req);
        if (updated is null) return false;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(op.EntityId);
        if (user is not null)
        {
            user.IsSynced = true;
            user.LastModifiedLocally = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return true;
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

    private async Task<bool> SyncMaterialCategoryAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateMaterialCategoryRequest>(op.Payload);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateMaterialCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncEquipmentCategoryAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateEquipmentCategoryRequest>(op.Payload);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateEquipmentCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncMaterialStockMovementAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordMaterialStockRequest>(op.Payload);
        if (req is null) return false;
        return await _api.RecordMaterialStockMovementAsync(op.EntityId, req) is not null;
    }

    private async Task<bool> SyncEquipmentAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteEquipmentAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateEquipmentRequest>(op.Payload);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateEquipmentAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload);
        return updateReq is not null && await _api.UpdateEquipmentAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncEquipmentHistoryAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordEquipmentEventRequest>(op.Payload);
        if (req is null) return false;
        return await _api.RecordEquipmentEventAsync(op.EntityId, req) is not null;
    }

    private async Task RunPeriodicSyncAsync()
    {
        while (await _timer.WaitForNextTickAsync())
            await SyncAsync();
    }
}
