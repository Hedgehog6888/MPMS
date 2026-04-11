using System.Diagnostics;
using System.Text.Json;
using System.Threading;
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

    Task QueueLocalActivityLogAsync(LocalActivityLog log);
}

public class SyncService : ISyncService
{
    /// <summary>
    /// Очередь хранит JSON с именами свойств по умолчанию (PascalCase).
    /// CaseInsensitive — чтобы подхватывать старые записи и не ломаться на смешанном регистре.
    /// Не используем camelCase/IgnoreNull при записи: иначе часть DTO при чтении не собирается в record.
    /// </summary>
    private static readonly JsonSerializerOptions PendingOpJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IApiService _api;
    private readonly IAuthService _auth;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(5));
    private readonly SemaphoreSlim _syncGate = new(1, 1);
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

    private static string NormalizeEquipmentStatus(string? status) => status switch
    {
        "3" => "Unavailable",
        _ => status ?? "Available"
    };

    private static bool IsUnavailableCondition(string? condition) =>
        condition is "NeedsMaintenance" or "Faulty";

    private static string ResolvePulledEquipmentStatus(string? status, string? condition)
    {
        var normalizedStatus = NormalizeEquipmentStatus(status);
        if (normalizedStatus is "InUse" or "CheckedOut" or "Retired")
            return normalizedStatus;

        return IsUnavailableCondition(condition) ? "Unavailable" : "Available";
    }

    private async Task PrepareSyncConnectionAsync()
    {
        _api.ClearLastUsersPullError();
        await _api.ProbeAsync();
        if (_api.IsOnline)
            await _auth.TryRefreshJwtIfNeededAsync(_api);
    }

    public async Task SyncAsync()
    {
        if (!_auth.IsAuthenticated) return;
        await _syncGate.WaitAsync();
        try
        {
            _isSyncing = true;
            await PrepareSyncConnectionAsync();
            try
            {
                await ProcessPendingOperationsAsync();
            }
            catch (Exception ex)
            {
                // Не блокируем pull с сервера из‑за сбоя исходящей очереди / складских ремапов
                Debug.WriteLine($"[Sync] ProcessPendingOperations: {ex}");
            }
            if (_api.IsOnline)
                await _auth.TryRefreshJwtIfNeededAsync(_api);
            await PullFromServerAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] PullFromServer: {ex}");
        }
        finally
        {
            _isSyncing = false;
            // Always fire so the UI reflects the current connectivity state,
            // even when an exception interrupted the sync.
            OnlineStatusChanged?.Invoke(this, _api.IsOnline);
            _syncGate.Release();
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
            Payload = JsonSerializer.Serialize(payload, PendingOpJson),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Try immediate sync if online
        _ = SyncAsync();
    }

    public Task QueueLocalActivityLogAsync(LocalActivityLog log)
    {
        var dto = new CreateSyncedActivityLogRequest(
            log.Id, log.UserId, log.ActorRole,
            log.UserName, log.UserInitials, log.UserColor,
            log.ActionType, log.ActionText, log.EntityType, log.EntityId, log.CreatedAt);
        return QueueOperationAsync("SyncedActivityLog", log.Id, SyncOperation.Create, dto);
    }

    // ── Pull latest data from server into local DB ────────────────────────────
    private async Task PullUsersAndRolesIntoLocalDbAsync(LocalDbContext db)
    {
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
                {
                    existing.Name = r.Name;
                    existing.Description = r.Description;
                }
            }
        }

        // Пользователи: API → локальная таблица Users (источник на сервере — та же БД, что у MPMS.API)
        var users = await _api.GetUsersAsync();
        if (!_api.IsOnline) return;
        if (users is not null)
            await UserListMergeHelper.ApplyPulledUsersAsync(db, users, _auth);

        // Сохранить роли и пользователей до остального pull: при ошибке в проектах/задачах/этапах
        // иначе один общий SaveChanges в конце не выполнится — локальная таблица Users останется пустой/устаревшей.
        await db.SaveChangesAsync();
    }

    private async Task PullFromServerAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        await PullUsersAndRolesIntoLocalDbAsync(db);
        if (!_api.IsOnline) return;

        // Projects
        var projects = await _api.GetProjectsAsync();
        if (projects is not null)
        {
            var existingProjects = await db.Projects.ToDictionaryAsync(p => p.Id);
            foreach (var p in projects)
            {
                if (existingProjects.TryGetValue(p.Id, out var local))
                {
                    if (local.IsSynced)
                    {
                        local.Name = p.Name;
                        local.Description = p.Description;
                        local.Client = p.Client;
                        local.Address = p.Address;
                        local.StartDate = p.StartDate;
                        local.EndDate = p.EndDate;
                        local.Status = Enum.Parse<ProjectStatus>(p.Status);
                        local.ManagerId = p.ManagerId;
                        local.ManagerName = p.ManagerName;
                        local.CreatedAt = p.CreatedAt;
                        local.UpdatedAt = p.UpdatedAt;
                        local.IsMarkedForDeletion = p.IsMarkedForDeletion;
                        local.IsArchived = p.IsArchived;
                        local.IsSynced = true;
                    }
                }
                else
                {
                    db.Projects.Add(new LocalProject
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        Client = p.Client,
                        Address = p.Address,
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        Status = Enum.Parse<ProjectStatus>(p.Status),
                        ManagerId = p.ManagerId,
                        ManagerName = p.ManagerName,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt,
                        IsMarkedForDeletion = p.IsMarkedForDeletion,
                        IsArchived = p.IsArchived,
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
                    if (local.IsSynced)
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
                        local.IsWrittenOff = m.IsWrittenOff;
                        local.WrittenOffAt = m.WrittenOffAt;
                        local.WrittenOffComment = m.WrittenOffComment;
                        local.IsSynced = true;
                    }
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
                        IsWrittenOff = m.IsWrittenOff,
                        WrittenOffAt = m.WrittenOffAt,
                        WrittenOffComment = m.WrittenOffComment,
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
                        Status = ResolvePulledEquipmentStatus(eq.Status, eq.Condition),
                        Condition = eq.Condition,
                        InventoryNumber = eq.InventoryNumber,
                        CreatedAt = eq.CreatedAt,
                        UpdatedAt = eq.UpdatedAt,
                        CheckedOutProjectId = eq.CheckedOutProjectId,
                        CheckedOutTaskId = eq.CheckedOutTaskId,
                        IsWrittenOff = eq.IsWrittenOff,
                        WrittenOffAt = eq.WrittenOffAt,
                        WrittenOffComment = eq.WrittenOffComment,
                        IsSynced = true
                    });
                }
                else
                {
                    if (existingEq.IsSynced)
                    {
                        existingEq.Name = eq.Name;
                        existingEq.Description = eq.Description;
                        existingEq.CategoryId = eq.CategoryId;
                        existingEq.CategoryName = eq.CategoryName;
                        existingEq.ImagePath = eq.ImagePath;
                        existingEq.Status = ResolvePulledEquipmentStatus(eq.Status, eq.Condition);
                        existingEq.Condition = eq.Condition;
                        existingEq.InventoryNumber = eq.InventoryNumber;
                        existingEq.UpdatedAt = eq.UpdatedAt;
                        existingEq.CheckedOutProjectId = eq.CheckedOutProjectId;
                        existingEq.CheckedOutTaskId = eq.CheckedOutTaskId;
                        existingEq.IsWrittenOff = eq.IsWrittenOff;
                        existingEq.WrittenOffAt = eq.WrittenOffAt;
                        existingEq.WrittenOffComment = eq.WrittenOffComment;
                        existingEq.IsSynced = true;
                    }
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

        // Task Stages + соисполнители (полный снимок по каждой задаче)
        var taskIds = await db.Tasks.Where(t => t.IsSynced).Select(t => t.Id).ToListAsync();
        var existingStages = await db.TaskStages.ToDictionaryAsync(s => s.Id);
        foreach (var taskId in taskIds)
        {
            var taskApi = await _api.GetTaskAsync(taskId);
            if (taskApi?.Stages is null) continue;

            var localTaskRow = await db.Tasks.FindAsync(taskId);
            if (localTaskRow?.IsSynced == true)
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
                    if (localStage.IsSynced)
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
                        localStage.IsSynced = true;
                    }
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

        // Сообщения обсуждений (инкрементально по времени)
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
                    CreatedAt = m.CreatedAt
                });
            }
        }

        // Синхронизированная лента активности
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
                    CreatedAt = a.CreatedAt
                });
            }
        }

        await db.SaveChangesAsync();
    }

    // ── Send pending operations to server ─────────────────────────────────────
    private async Task ProcessPendingOperationsAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        try
        {
            await EnsureWarehouseCategoriesReadyAsync(db);
        }
        catch (Exception ex)
        {
            // Не блокируем отправку остальной очереди из‑за ремапов категорий склада
            Debug.WriteLine($"[Sync] EnsureWarehouseCategoriesReadyAsync: {ex}");
        }

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
                    Payload = JsonSerializer.Serialize(new CreateMaterialCategoryRequest(local.Name, local.Id), PendingOpJson),
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
                    Payload = JsonSerializer.Serialize(new CreateEquipmentCategoryRequest(local.Name, local.Id), PendingOpJson),
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
                var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload, PendingOpJson);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, PendingOpJson);
            }
            else if (op.OperationType == SyncOperation.Update)
            {
                var req = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload, PendingOpJson);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, PendingOpJson);
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
                var req = JsonSerializer.Deserialize<CreateEquipmentRequest>(op.Payload, PendingOpJson);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, PendingOpJson);
            }
            else if (op.OperationType == SyncOperation.Update)
            {
                var req = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload, PendingOpJson);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, PendingOpJson);
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
                "SyncedActivityLog" => await SyncSyncedActivityLogAsync(op),
                "DiscussionMessage" => await SyncDiscussionMessageAsync(op),
                "TaskAssignees" => await SyncTaskAssigneesAsync(op),
                "StageAssignees" => await SyncStageAssigneesAsync(op),
                _             => true
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] {op.EntityType} {op.OperationType} {op.EntityId}: {ex}");
            try
            {
                var msg = ex.Message;
                op.ErrorMessage = msg.Length > 500 ? msg[..500] : msg;
            }
            catch { /* ignore */ }
            return false;
        }
    }

    private async Task<bool> SyncProjectAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteProjectAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateProjectRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateProjectAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateProjectRequest>(op.Payload, PendingOpJson);
        return updateReq is not null && await _api.UpdateProjectAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncTaskAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteTaskAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateTaskRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateTaskAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateTaskRequest>(op.Payload, PendingOpJson);
        return updateReq is not null && await _api.UpdateTaskAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncStageAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteStageAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateStageRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateStageAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateStageRequest>(op.Payload, PendingOpJson);
        return updateReq is not null && await _api.UpdateStageAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncUserAvatarAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var payload = JsonSerializer.Deserialize<UploadAvatarRequest>(op.Payload, PendingOpJson);
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
        var req = JsonSerializer.Deserialize<UpdateUserRequest>(op.Payload, PendingOpJson);
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
            var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            return await _api.CreateMaterialAsync(req) is not null;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload, PendingOpJson);
        return updateReq is not null && await _api.UpdateMaterialAsync(op.EntityId, updateReq) is not null;
    }

    private async Task<bool> SyncMaterialCategoryAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateMaterialCategoryRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateMaterialCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncEquipmentCategoryAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateEquipmentCategoryRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateEquipmentCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncMaterialStockMovementAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordMaterialStockRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.RecordMaterialStockMovementAsync(op.EntityId, req) is not null;
    }

    private async Task<bool> SyncEquipmentAsync(PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteEquipmentAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateEquipmentRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateEquipmentAsync(req);
            if (created is null) return false;
            await using var db = await _dbFactory.CreateDbContextAsync();
            var local = await db.Equipments.FindAsync(op.EntityId);
            if (local is not null)
            {
                local.IsSynced = true;
                await db.SaveChangesAsync();
            }
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload, PendingOpJson);
        if (updateReq is null) return false;
        var updated = await _api.UpdateEquipmentAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        await using var db2 = await _dbFactory.CreateDbContextAsync();
        var local2 = await db2.Equipments.FindAsync(op.EntityId);
        if (local2 is not null)
        {
            local2.IsSynced = true;
            await db2.SaveChangesAsync();
        }
        return true;
    }

    private async Task<bool> SyncEquipmentHistoryAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordEquipmentEventRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.RecordEquipmentEventAsync(op.EntityId, req) is not null;
    }

    private async Task<bool> SyncSyncedActivityLogAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateSyncedActivityLogRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.PostSyncedActivityLogAsync(req) is not null;
    }

    /// <summary>Пустой Guid в payload давал на API оба флага (task+project) и 400.</summary>
    private static CreateDiscussionMessageRequest NormalizeDiscussionRequest(CreateDiscussionMessageRequest req)
    {
        var taskId = req.TaskId;
        var projectId = req.ProjectId;
        if (taskId == Guid.Empty) taskId = null;
        if (projectId == Guid.Empty) projectId = null;
        return req with { TaskId = taskId, ProjectId = projectId };
    }

    private async Task<bool> SyncDiscussionMessageAsync(PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateDiscussionMessageRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        req = NormalizeDiscussionRequest(req);
        return await _api.PostDiscussionMessageAsync(req) is not null;
    }

    private async Task<bool> SyncTaskAssigneesAsync(PendingOperation op)
    {
        var req = JsonSerializer.Deserialize<ReplaceTaskAssigneesRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.ReplaceTaskAssigneesAsync(op.EntityId, req);
    }

    private async Task<bool> SyncStageAssigneesAsync(PendingOperation op)
    {
        var req = JsonSerializer.Deserialize<ReplaceStageAssigneesRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.ReplaceStageAssigneesAsync(op.EntityId, req);
    }

    private async Task RunPeriodicSyncAsync()
    {
        while (await _timer.WaitForNextTickAsync())
            await SyncAsync();
    }
}
