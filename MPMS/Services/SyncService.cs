using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.IO;
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

    /// <summary>SQLite/EF и часть DTO отдают UTC без Kind=Utc — приводим к UTC для сравнений и отображения.</summary>
    private static DateTime NormalizeUtcInstant(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
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

            await using (var dbInit = await _dbFactory.CreateDbContextAsync())
            {
                // Enable WAL mode for better concurrency (UI stays responsive during sync)
                await dbInit.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            }
            
            // 1. Send local changes to server
            try
            {
                await ProcessPendingOperationsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] ProcessPendingOperations: {ex}");
            }

            if (!_api.IsOnline) return;

            // 2. Pull latest data from server
            await PullFromServerAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] SyncAsync Error: {ex}");
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

        // Пользователи: API → локальная таблица Users
        var users = await _api.GetUsersAsync();
        if (!_api.IsOnline) return;
        if (users is not null)
            await UserListMergeHelper.ApplyPulledUsersAsync(db, users, _auth);

        // NOTE: We don't SaveChanges here anymore, as it's handled by the outer transaction in PullFromServerAsync
    }

    private async Task PullFromServerAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        
        // Use a transaction to ensure database consistency (no intermediate state)
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            await PullUsersAndRolesIntoLocalDbAsync(db);
        if (!_api.IsOnline) return;

        await PullFilesIntoLocalDbAsync(db);
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
                    else
                    {
                        // Очередь ещё не прошла: не затирать IsSynced, но выровнять строку с сервером (архив, сроки и т.д.).
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

            // Проектов нет в ответе API — на сервере удалены; убрать локально (иначе «воскрешение» при pull).
            var serverProjectIds = projects.Select(p => p.Id).ToHashSet();
            var orphanProjectIds = await db.Projects
                .Where(p => p.IsSynced && !serverProjectIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync();
            foreach (var pid in orphanProjectIds)
                await LocalDbGraphDeletion.PermanentlyDeleteProjectGraphAsync(db, pid);
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
                        local.IsArchived = m.IsArchived;
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
                        IsArchived = m.IsArchived,
                        IsSynced = true
                    });
                }
            }

            var serverMaterialIds = materials.Select(m => m.Id).ToHashSet();
            var orphanMaterialIds = await db.Materials
                .Where(m => m.IsSynced && !serverMaterialIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync();
            foreach (var mid in orphanMaterialIds)
                await LocalDbGraphDeletion.PermanentlyDeleteMaterialGraphAsync(db, mid);
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
                        IsArchived = eq.IsArchived,
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
                        existingEq.IsArchived = eq.IsArchived;
                        existingEq.IsSynced = true;
                    }
                }
            }

            var serverEquipIds = equips.Select(e => e.Id).ToHashSet();
            var orphanEquipIds = await db.Equipments
                .Where(e => e.IsSynced && !serverEquipIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync();
            foreach (var eid in orphanEquipIds)
                await LocalDbGraphDeletion.PermanentlyDeleteEquipmentGraphAsync(db, eid);
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

        // Task Stages + соисполнители (полный снимок по каждой задаче).
        // Нельзя ограничивать только IsSynced: после восстановления из архива IsSynced=false, иначе этапы
        // вообще не подтягиваются с сервера, пока очередь не отработает.
        var taskIds = await db.Tasks.Select(t => t.Id).ToListAsync();
        var existingStages = await db.TaskStages.ToDictionaryAsync(s => s.Id);
        foreach (var taskId in taskIds)
        {
            var taskApi = await _api.GetTaskAsync(taskId);
            if (taskApi?.Stages is null) continue;

            var localTaskRow = await db.Tasks.FindAsync(taskId);
            if (localTaskRow is not null)
            {
                // Обновляем и при IsSynced=false (восстановление из архива и т.п.), не меняя флаг IsSynced здесь.
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
                    CreatedAt = NormalizeUtcInstant(m.CreatedAt)
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
                    CreatedAt = NormalizeUtcInstant(a.CreatedAt)
                });
            }
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        Debug.WriteLine($"[Sync] PullFromServer transaction rolled back: {ex.Message}");
        throw;
    }
}

    // ── Send pending operations to server ─────────────────────────────────────
    private async Task ProcessPendingOperationsAsync()
    {
        if (!_api.IsOnline) return;

        try
        {
            await EnsureWarehouseCategoriesReadyAsync();
        }
        catch (Exception ex)
        {
            // Не блокируем отправку остальной очереди из‑за ремапов категорий склада
            Debug.WriteLine($"[Sync] EnsureWarehouseCategoriesReadyAsync: {ex}");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        await RecoverWarehouseFailedOperationsAsync(db);

        var pending = await db.PendingOperations
            .Where(p => !p.IsFailed)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();

        int consecutiveNetworkErrors = 0;
        foreach (var op in pending)
        {
            if (!_api.IsOnline) break;
            if (consecutiveNetworkErrors >= 3)
            {
                Debug.WriteLine("[Sync] Too many consecutive errors, stopping pending operations processing.");
                break;
            }

            try
            {
                var success = await ProcessOperationAsync(db, op);
                if (success)
                {
                    db.PendingOperations.Remove(op);
                    consecutiveNetworkErrors = 0;
                }
                else
                {
                    op.RetryCount++;
                    if (op.RetryCount >= 5) op.IsFailed = true;
                    consecutiveNetworkErrors++;
                    
                    // Small delay after a failure to let the connection "breathe"
                    await Task.Delay(1000); 
                }
                
                // Save progress after EACH operation. This ensures that if the connection 
                // drops, we don't repeat successful operations on the next sync.
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                op.ErrorMessage = ex.Message;
                op.RetryCount++;
                if (op.RetryCount >= 10) op.IsFailed = true;
                Debug.WriteLine($"[Sync] Operation {op.Id} failed: {ex.Message}");
                await db.SaveChangesAsync();
            }
        }

        await PushOrphanedFilesAsync(db);
        await db.SaveChangesAsync();
    }

    private async Task PushOrphanedFilesAsync(LocalDbContext db)
    {
        // Find files that are not synced and don't have a pending "Create" operation
        var unsyncedFileIds = await db.Files
            .Where(f => !f.IsSynced)
            .Select(f => f.Id)
            .ToListAsync();

        if (!unsyncedFileIds.Any()) return;

        var pendingFileIds = await db.PendingOperations
            .Where(op => op.EntityType == "File" && op.OperationType == SyncOperation.Create)
            .Select(op => op.EntityId)
            .ToListAsync();

        var orphanedIds = unsyncedFileIds.Except(pendingFileIds).ToList();

        foreach (var id in orphanedIds)
        {
            var file = await db.Files.FindAsync(id);
            if (file == null) continue;

            var dto = new FileDto(file.Id, file.FileName, file.FileType ?? "", file.FileSize,
                file.UploadedById, file.UploadedByName, file.ProjectId, file.TaskId, file.StageId,
                file.CreatedAt, file.OriginalCreatedAt);

            var payload = JsonSerializer.Serialize(dto, PendingOpJson);
            db.PendingOperations.Add(new PendingOperation
            {
                Id = Guid.NewGuid(),
                EntityType = "File",
                EntityId = id,
                OperationType = SyncOperation.Create,
                Payload = payload,
                CreatedAt = DateTime.UtcNow
            });
        }
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

    private async Task EnsureWarehouseCategoriesReadyAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // ... (rest of method)
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

    private async Task<bool> ProcessOperationAsync(LocalDbContext db, PendingOperation op)
    {
        try
        {
            return op.EntityType switch
            {
                "Project"     => await SyncProjectAsync(db, op),
                "Task"        => await SyncTaskAsync(db, op),
                "Stage"       => await SyncStageAsync(db, op),
                "Material"    => await SyncMaterialAsync(db, op),
                "MaterialCategory" => await SyncMaterialCategoryAsync(db, op),
                "EquipmentCategory" => await SyncEquipmentCategoryAsync(db, op),
                "MaterialStockMovement" => await SyncMaterialStockMovementAsync(db, op),
                "Equipment" => await SyncEquipmentAsync(db, op),
                "EquipmentHistory" => await SyncEquipmentHistoryAsync(db, op),
                "User"        => await SyncUserAvatarAsync(db, op),
                "UserProfile" => await SyncUserProfileAsync(db, op),
                "SyncedActivityLog" => await SyncSyncedActivityLogAsync(db, op),
                "DiscussionMessage" => await SyncDiscussionMessageAsync(db, op),
                "TaskAssignees" => await SyncTaskAssigneesAsync(db, op),
                "StageAssignees" => await SyncStageAssigneesAsync(db, op),
                "File"        => await SyncFileAsync(db, op),
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

    private async Task<bool> SyncProjectAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteProjectAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateProjectRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateProjectAsync(req);
            if (created is null) return false;
            
            var local = await db.Projects.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateProjectRequest>(op.Payload, PendingOpJson);
        if (updateReq is null) return false;
        var updated = await _api.UpdateProjectAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.Projects.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }

    private async Task<bool> SyncTaskAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteTaskAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateTaskRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateTaskAsync(req);
            if (created is null) return false;
            
            var local = await db.Tasks.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateTaskRequest>(op.Payload, PendingOpJson);
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
            var req = JsonSerializer.Deserialize<CreateStageRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateStageAsync(req);
            if (created is null) return false;
            
            var local = await db.TaskStages.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateStageRequest>(op.Payload, PendingOpJson);
        if (updateReq is null) return false;
        var updated = await _api.UpdateStageAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.TaskStages.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }

    private async Task<bool> SyncUserAvatarAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var payload = JsonSerializer.Deserialize<UploadAvatarRequest>(op.Payload, PendingOpJson);
        if (payload?.AvatarData is null || payload.AvatarData.Length == 0) return true;
        var ok = await _api.UploadUserAvatarAsync(op.EntityId, payload.AvatarData);
        if (ok)
        {
            var user = await db.Users.FindAsync(op.EntityId);
            if (user is not null) user.IsSynced = true;
        }
        return ok;
    }

    private async Task<bool> SyncUserProfileAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Update) return true;
        var req = JsonSerializer.Deserialize<UpdateUserRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        var updated = await _api.UpdateUserAsync(op.EntityId, req);
        if (updated is null) return false;
        
        var user = await db.Users.FindAsync(op.EntityId);
        if (user is not null)
        {
            user.IsSynced = true;
            user.LastModifiedLocally = DateTime.UtcNow;
        }

        return true;
    }

    private async Task<bool> SyncMaterialAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteMaterialAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload, PendingOpJson);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateMaterialAsync(req);
            if (created is not null)
            {
                var local = await db.Materials.FindAsync(op.EntityId);
                if (local is not null) local.IsSynced = true;
                return true;
            }
            return false;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload, PendingOpJson);
        if (updateReq is not null && await _api.UpdateMaterialAsync(op.EntityId, updateReq) is not null)
        {
            var local = await db.Materials.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            return true;
        }
        return false;
    }

    private async Task<bool> SyncMaterialCategoryAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateMaterialCategoryRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateMaterialCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncEquipmentCategoryAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateEquipmentCategoryRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateEquipmentCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncMaterialStockMovementAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordMaterialStockRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.RecordMaterialStockMovementAsync(op.EntityId, req) is not null;
    }

    private async Task<bool> SyncEquipmentAsync(LocalDbContext db, PendingOperation op)
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
            
            var local = await db.Equipments.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload, PendingOpJson);
        if (updateReq is null) return false;
        var updated = await _api.UpdateEquipmentAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.Equipments.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }

    private async Task<bool> SyncEquipmentHistoryAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordEquipmentEventRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.RecordEquipmentEventAsync(op.EntityId, req) is not null;
    }

    private async Task<bool> SyncSyncedActivityLogAsync(LocalDbContext db, PendingOperation op)
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

    private async Task<bool> SyncDiscussionMessageAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateDiscussionMessageRequest>(op.Payload, PendingOpJson);
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

    private async Task<bool> SyncTaskAssigneesAsync(LocalDbContext db, PendingOperation op)
    {
        var req = JsonSerializer.Deserialize<ReplaceTaskAssigneesRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.ReplaceTaskAssigneesAsync(op.EntityId, req);
    }

    private async Task<bool> SyncStageAssigneesAsync(LocalDbContext db, PendingOperation op)
    {
        var req = JsonSerializer.Deserialize<ReplaceStageAssigneesRequest>(op.Payload, PendingOpJson);
        if (req is null) return false;
        return await _api.ReplaceStageAssigneesAsync(op.EntityId, req);
    }

    private async Task<bool> SyncFileAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteFileAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var meta = JsonSerializer.Deserialize<FileDto>(op.Payload, PendingOpJson);
            if (meta is null) return false;

            var local = await db.Files.FindAsync(op.EntityId);
            if (local is null) return false; 
            if (local.FileData is null || local.FileData.Length == 0) return true; 

            var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{local.FileName}");
            await File.WriteAllBytesAsync(tempPath, local.FileData);
            try
            {
                var uploaded = await _api.UploadFileAsync(tempPath, local.ProjectId, local.TaskId, local.StageId, local.OriginalCreatedAt, local.Id);
                if (uploaded is not null)
                {
                    local.IsSynced = true;
                    return true;
                }
                return false;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        return true;
    }

    private async Task PullFilesIntoLocalDbAsync(LocalDbContext db)
    {
        var files = await _api.GetFilesAsync();
        if (files is null || !_api.IsOnline) return;

        var existingFiles = await db.Files.ToDictionaryAsync(f => f.Id);
        foreach (var f in files)
        {
            if (existingFiles.TryGetValue(f.Id, out var local))
            {
                if (local.IsSynced)
                {
                    local.FileName = f.FileName;
                    local.FileType = f.FileType;
                    local.FileSize = f.FileSize;
                    local.UploadedByName = f.UploadedByName;
                    local.ProjectId = f.ProjectId;
                    local.TaskId = f.TaskId;
                    local.StageId = f.StageId;
                    local.CreatedAt = f.CreatedAt;
                    local.OriginalCreatedAt = f.OriginalCreatedAt;
                }
            }
            else
            {
                db.Files.Add(new LocalFile
                {
                    Id = f.Id,
                    FileName = f.FileName,
                    FileType = f.FileType,
                    FileSize = f.FileSize,
                    UploadedById = f.UploadedById,
                    UploadedByName = f.UploadedByName,
                    ProjectId = f.ProjectId,
                    TaskId = f.TaskId,
                    StageId = f.StageId,
                    CreatedAt = f.CreatedAt,
                    OriginalCreatedAt = f.OriginalCreatedAt,
                    IsSynced = true
                });
            }
        }

        var serverFileIds = files.Select(f => f.Id).ToHashSet();
        var orphanFiles = await db.Files
            .Where(f => f.IsSynced && !serverFileIds.Contains(f.Id))
            .ToListAsync();
        db.Files.RemoveRange(orphanFiles);
    }

    private async Task RunPeriodicSyncAsync()
    {
        while (await _timer.WaitForNextTickAsync())
            await SyncAsync();
    }
}
