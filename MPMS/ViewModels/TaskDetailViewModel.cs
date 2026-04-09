using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MaterialStockOperationType = MPMS.Models.MaterialStockOperationType;
using EquipmentHistoryEventType = MPMS.Models.EquipmentHistoryEventType;

namespace MPMS.ViewModels;

public partial class TaskDetailViewModel : ViewModelBase
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;

    [ObservableProperty] private LocalTask? _task;
    [ObservableProperty] private ObservableCollection<LocalTaskStage> _stages = [];
    [ObservableProperty] private ObservableCollection<LocalStageMaterial> _allMaterials = [];
    [ObservableProperty] private ObservableCollection<LocalStageMaterial> _selectedStageMaterials = [];
    [ObservableProperty] private ObservableCollection<LocalFile> _files = [];
    [ObservableProperty] private ObservableCollection<LocalMessage> _messages = [];
    [ObservableProperty] private LocalTaskStage? _selectedStage;
    [ObservableProperty] private string _activeTab = "Stages";
    [ObservableProperty] private bool _hasNoStages = true;
    [ObservableProperty] private bool _hasNoMaterials = true;
    [ObservableProperty] private bool _hasNoFiles = true;
    [ObservableProperty] private string _stagesTabLabel = "Этапы";

    public TaskDetailViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    private bool CanMarkStageDeletion() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager" or "Foreman";

    private bool CanDeleteStage() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    public void SetTask(LocalTask task) => Task = task;

    public async Task LoadAsync()
    {
        if (Task is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();

        var projectEntity = await db.Projects.FindAsync(Task.ProjectId);
        Task.ProjectIsMarkedForDeletion = projectEntity?.IsMarkedForDeletion ?? false;

        var stages = await db.TaskStages
            .Where(s => s.TaskId == Task.Id)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        Stages = new ObservableCollection<LocalTaskStage>(stages);
        HasNoStages = stages.Count == 0;
        StagesTabLabel = stages.Count > 0 ? $"Этапы ({stages.Count})" : "Этапы";

        // Load all materials for all stages of this task
        var stageIds = stages.Select(s => s.Id).ToList();
        var mats = await db.StageMaterials
            .Where(sm => stageIds.Contains(sm.StageId))
            .ToListAsync();

        // Populate StageName for display
        foreach (var mat in mats)
        {
            var stage = stages.FirstOrDefault(s => s.Id == mat.StageId);
            mat.StageName = stage?.Name ?? "—";
        }

        AllMaterials = new ObservableCollection<LocalStageMaterial>(mats);
        HasNoMaterials = mats.Count == 0;

        if (SelectedStage is not null)
            await LoadStageMaterialsAsync(SelectedStage.Id);

        // Load files for this task
        var files = await db.Files
            .Where(f => f.TaskId == Task.Id)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
        Files = new ObservableCollection<LocalFile>(files);
        HasNoFiles = files.Count == 0;

        foreach (var s in stages)
        {
            s.TaskIsMarkedForDeletion = Task.IsMarkedForDeletion;
            s.ProjectIsMarkedForDeletion = Task.ProjectIsMarkedForDeletion;
        }

        // Refresh task progress from active stages
        ProgressCalculator.ApplyTaskMetrics(Task, stages);
        OnPropertyChanged(nameof(Task));

        // Load messages for this task with AvatarData from Users
        var messages = await db.Messages
            .Where(m => m.TaskId == Task.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        var msgUserIds = messages.Select(m => m.UserId).Distinct().ToList();
        if (msgUserIds.Count > 0)
        {
            var msgUserAvatars = await db.Users.Where(u => msgUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToListAsync();
            var msgAvDict = msgUserAvatars.ToDictionary(u => u.Id);
            foreach (var msg in messages)
            {
                if (msgAvDict.TryGetValue(msg.UserId, out var av))
                {
                    msg.AvatarData = av.AvatarData;
                    msg.AvatarPath = av.AvatarPath;
                }
            }
        }
        Messages = new ObservableCollection<LocalMessage>(messages);
    }

    public async Task SendMessageAsync(string text)
    {
        if (Task is null || string.IsNullOrWhiteSpace(text)) return;
        var auth = App.Services.GetRequiredService<IAuthService>();
        await using var db = await _dbFactory.CreateDbContextAsync();

        var userName = auth.UserName ?? "—";
        var initials = string.IsNullOrEmpty(userName) ? "?"
            : string.Concat(userName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpperInvariant() : ""));
        if (string.IsNullOrEmpty(initials)) initials = "?";
        var msg = new LocalMessage
        {
            Id = Guid.NewGuid(),
            TaskId = Task.Id,
            UserId = auth.UserId ?? Guid.Empty,
            UserName = userName,
            UserInitials = initials,
            UserColor = "#1B6EC2",
            UserRole = ProjectDetailViewModel.RoleToRussian(auth.UserRole),
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        if (auth.UserId.HasValue)
        {
            var avatar = await db.Users
                .Where(u => u.Id == auth.UserId.Value)
                .Select(u => new { u.AvatarData, u.AvatarPath })
                .FirstOrDefaultAsync();
            if (avatar is not null)
            {
                msg.AvatarData = avatar.AvatarData;
                msg.AvatarPath = avatar.AvatarPath;
            }
        }

        db.Messages.Add(msg);
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("DiscussionMessage", msg.Id, SyncOperation.Create,
            new CreateDiscussionMessageRequest(msg.Id, msg.TaskId, msg.ProjectId, msg.Text, msg.CreatedAt));
        await LogActivityAsync(db, $"Сообщение в задаче «{Task.Name}»", "Message", msg.Id, ActivityActionKind.Message);
        Messages.Add(msg);
    }

    private async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId, string? actionType = null)
    {
        var session = await db.AuthSessions.FindAsync(1);
        var userName = session?.UserName ?? "Система";
        var userId = session?.UserId;
        var actorRole = session?.UserRole;
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : userName.Length > 0 ? $"{userName[0]}" : "?";

        var log = new LocalActivityLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActorRole = actorRole,
            UserName = userName,
            UserInitials = initials.ToUpper(),
            UserColor = "#1B6EC2",
            ActionType = actionType,
            ActionText = actionText,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        await _sync.QueueLocalActivityLogAsync(log);
    }

    partial void OnSelectedStageChanged(LocalTaskStage? value)
    {
        if (value is not null)
            _ = LoadStageMaterialsAsync(value.Id);
        else
            SelectedStageMaterials = [];
    }

    private async Task LoadStageMaterialsAsync(Guid stageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var mats = await db.StageMaterials
            .Where(sm => sm.StageId == stageId)
            .ToListAsync();
        SelectedStageMaterials = new ObservableCollection<LocalStageMaterial>(mats);
    }

    public async Task SaveNewStageAsync(CreateStageRequest req, Guid localId)
    {
        if (!DueDatePolicy.IsAllowed(req.DueDate))
            throw new ArgumentException(DueDatePolicy.PastNotAllowedMessage);

        await using var db = await _dbFactory.CreateDbContextAsync();

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        var stage = new LocalTaskStage
        {
            Id = localId,
            TaskId = req.TaskId,
            Name = req.Name,
            Description = req.Description,
            AssignedUserId = req.AssignedUserId,
            AssignedUserName = assignedName,
            DueDate = req.DueDate,
            Status = StageStatus.Planned,
            ServiceTemplateId = req.ServiceTemplateId,
            WorkQuantity = req.WorkQuantity,
            WorkPricePerUnit = req.WorkPricePerUnit ?? 0m,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastModifiedLocally = DateTime.UtcNow
        };

        db.TaskStages.Add(stage);
        await ReplaceLocalStageServicesAsync(db, localId, req.ServiceItems);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Stage", localId, SyncOperation.Create,
            req with { Id = localId });

        await LogActivityAsync(db, $"Создан этап «{req.Name}»", "Stage", localId, ActivityActionKind.Created);
        await LoadAsync();
        await UpdateTaskProgressAsync();
    }

    public async Task SaveUpdatedStageAsync(Guid id, UpdateStageRequest req)
    {
        if (!DueDatePolicy.IsAllowed(req.DueDate))
            throw new ArgumentException(DueDatePolicy.PastNotAllowedMessage);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(id);
        if (stage is null) return;
        if (stage.Status == StageStatus.Completed) return;
        var wasReservedByStage = ShouldReserveStageEquipment(stage);

        stage.Name = req.Name;
        stage.Description = req.Description;
        stage.AssignedUserId = req.AssignedUserId;
        stage.Status = req.Status;
        stage.DueDate = req.DueDate;
        stage.ServiceTemplateId = req.ServiceTemplateId;
        stage.WorkQuantity = req.WorkQuantity;
        stage.WorkPricePerUnit = req.WorkPricePerUnit;
        stage.IsSynced = false;
        stage.UpdatedAt = DateTime.UtcNow;
        stage.LastModifiedLocally = DateTime.UtcNow;

        if (req.ServiceItems is not null)
            await ReplaceLocalStageServicesAsync(db, id, req.ServiceItems);

        var isReservedByStage = ShouldReserveStageEquipment(stage);
        if (wasReservedByStage != isReservedByStage)
        {
            var eqIds = await db.StageEquipments
                .Where(x => x.StageId == id)
                .Select(x => x.EquipmentId)
                .Distinct()
                .ToListAsync();
            var task = await db.Tasks.FindAsync(stage.TaskId);
            var projectId = task?.ProjectId;
            await SetStageEquipmentStateAsync(db, stage, eqIds, reserve: isReservedByStage, projectId);
        }

        await db.SaveChangesAsync();
        var syncStageReq = req with
        {
            IsMarkedForDeletion = stage.IsMarkedForDeletion,
            IsArchived = stage.IsArchived
        };
        await _sync.QueueOperationAsync("Stage", id, SyncOperation.Update, syncStageReq);
        await LogActivityAsync(db, $"Обновлён этап «{req.Name}»", "Stage", id, ActivityActionKind.Updated);
        await LoadAsync();
        await UpdateTaskProgressAsync();
    }

    private static async System.Threading.Tasks.Task ReplaceLocalStageServicesAsync(
        LocalDbContext db, Guid stageId, IReadOnlyList<StageServiceItemRequest>? items)
    {
        var existing = await db.StageServices.Where(x => x.StageId == stageId).ToListAsync();
        db.StageServices.RemoveRange(existing);
        if (items is null || items.Count == 0) return;

        foreach (var item in items)
        {
            var tpl = await db.ServiceTemplates.FindAsync(item.ServiceTemplateId);
            var price = item.PricePerUnit ?? tpl?.BasePrice ?? 0m;
            db.StageServices.Add(new LocalStageService
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                ServiceTemplateId = item.ServiceTemplateId,
                ServiceName = tpl?.Name ?? "—",
                ServiceDescription = tpl?.Description,
                Unit = tpl?.Unit,
                Quantity = item.Quantity,
                PricePerUnit = price,
                IsSynced = false,
                LastModifiedLocally = DateTime.UtcNow
            });
        }
    }

    public async Task ReplaceStageAssigneesAsync(Guid stageId, IReadOnlyList<(Guid UserId, string UserName)> assignees)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.StageAssignees.Where(a => a.StageId == stageId).ToListAsync();
        db.StageAssignees.RemoveRange(existing);
        foreach (var a in assignees)
        {
            db.StageAssignees.Add(new LocalStageAssignee
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                UserId = a.UserId,
                UserName = a.UserName
            });
        }
        await db.SaveChangesAsync();

        var rows = await db.StageAssignees.Where(x => x.StageId == stageId).ToListAsync();
        await _sync.QueueOperationAsync("StageAssignees", stageId, SyncOperation.Update,
            new ReplaceStageAssigneesRequest(rows.Select(r => new AssigneeSyncItemDto(r.Id, r.UserId)).ToList()));
    }

    public async Task ReplaceStageMaterialsAsync(Guid stageId, IReadOnlyList<LocalStageMaterial> materials)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(stageId);
        if (stage is null) return;
        var task = await db.Tasks.FindAsync(stage.TaskId);
        var existing = await db.StageMaterials.Where(m => m.StageId == stageId).ToListAsync();

        var existingByMaterial = existing
            .GroupBy(m => m.MaterialId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var incomingByMaterial = materials
            .GroupBy(m => m.MaterialId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        // Синхронизация склада при редактировании этапа:
        // увеличение в этапе -> расход со склада, уменьшение -> возврат на склад.
        var materialIds = existingByMaterial.Keys
            .Union(incomingByMaterial.Keys)
            .Distinct()
            .ToList();
        foreach (var materialId in materialIds)
        {
            var newQty = incomingByMaterial.GetValueOrDefault(materialId, 0m);
            var oldQty = existingByMaterial.GetValueOrDefault(materialId, 0m);
            var delta = newQty - oldQty;
            if (delta == 0m) continue;

            var mat = await db.Materials.FindAsync(materialId);
            if (mat is null) continue;
            var isConsumption = delta > 0m;
            var stockDelta = isConsumption ? -delta : Math.Abs(delta);
            var qtyAbs = Math.Abs(delta);
            var unitSuffix = string.IsNullOrWhiteSpace(mat.Unit) ? string.Empty : $" {mat.Unit}";
            mat.Quantity = Math.Max(0m, mat.Quantity + stockDelta);
            mat.UpdatedAt = DateTime.UtcNow;
            mat.IsSynced = false;

            db.MaterialStockMovements.Add(new LocalMaterialStockMovement
            {
                Id = Guid.NewGuid(),
                MaterialId = materialId,
                OccurredAt = DateTime.UtcNow,
                Delta = stockDelta,
                QuantityAfter = mat.Quantity,
                OperationType = isConsumption ? "StageConsumption" : "StageReturn",
                Comment = isConsumption
                    ? $"Списание из этапа: {stage.Name} ({qtyAbs:G}{unitSuffix})"
                    : $"Добавление из этапа: {stage.Name} ({qtyAbs:G}{unitSuffix})",
                UserId = _auth.UserId,
                UserName = _auth.UserName,
                ProjectId = task?.ProjectId,
                TaskId = stage.TaskId
            });

            await _sync.QueueOperationAsync("MaterialStockMovement", materialId, SyncOperation.Create,
                new RecordMaterialStockRequest(
                    Delta: stockDelta,
                    OperationType: isConsumption
                        ? MaterialStockOperationType.Consumption
                        : MaterialStockOperationType.ReturnToStock,
                    Comment: isConsumption
                        ? $"Списание из этапа: {stage.Name} ({qtyAbs:G}{unitSuffix})"
                        : $"Добавление из этапа: {stage.Name} ({qtyAbs:G}{unitSuffix})",
                    ProjectId: task?.ProjectId,
                    TaskId: stage.TaskId));
        }

        db.StageMaterials.RemoveRange(existing);
        foreach (var m in materials)
        {
            m.StageId = stageId;
            m.IsSynced = false;
            m.LastModifiedLocally = DateTime.UtcNow;
            db.StageMaterials.Add(m);
        }
        await db.SaveChangesAsync();
    }

    public async Task ReplaceStageEquipmentsAsync(Guid stageId, IReadOnlyList<LocalStageEquipment> equipments)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(stageId);
        if (stage is null) return;
        var task = await db.Tasks.FindAsync(stage.TaskId);
        var projectId = task?.ProjectId;

        var existing = await db.StageEquipments.Where(x => x.StageId == stageId).ToListAsync();
        var existingIds = existing.Select(x => x.EquipmentId).ToHashSet();
        var incomingIds = equipments.Select(x => x.EquipmentId).ToHashSet();
        var removedIds = existingIds.Except(incomingIds).ToList();
        var addedIds = incomingIds.Except(existingIds).ToList();

        db.StageEquipments.RemoveRange(existing.Where(x => removedIds.Contains(x.EquipmentId)));
        foreach (var eq in equipments.Where(x => addedIds.Contains(x.EquipmentId)))
        {
            eq.StageId = stageId;
            eq.IsSynced = false;
            eq.LastModifiedLocally = DateTime.UtcNow;
            db.StageEquipments.Add(eq);
        }

        if (addedIds.Count > 0)
            await SetStageEquipmentStateAsync(db, stage, addedIds, reserve: ShouldReserveStageEquipment(stage), projectId);
        if (removedIds.Count > 0)
            await SetStageEquipmentStateAsync(db, stage, removedIds, reserve: false, projectId);

        await db.SaveChangesAsync();
    }

    private async Task SetStageEquipmentStateAsync(
        LocalDbContext db,
        LocalTaskStage stage,
        IReadOnlyList<Guid> equipmentIds,
        bool reserve,
        Guid? projectId)
    {
        if (equipmentIds.Count == 0) return;

        foreach (var eqId in equipmentIds)
        {
            var eq = await db.Equipments.FindAsync(eqId);
            if (eq is null || eq.IsWrittenOff) continue;

            if (reserve)
            {
                // Не перехватываем оборудование, если оно уже закреплено за другой задачей.
                if (eq.CheckedOutTaskId.HasValue && eq.CheckedOutTaskId != stage.TaskId)
                    continue;

                if ((eq.Status == "InUse" || eq.Status == "Unavailable") && eq.CheckedOutTaskId == stage.TaskId)
                    continue;

                var prevStatus = eq.Status;
                eq.Status = "Unavailable";
                eq.CheckedOutTaskId = stage.TaskId;
                eq.CheckedOutProjectId = projectId;
                eq.UpdatedAt = DateTime.UtcNow;
                eq.IsSynced = false;

                db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
                {
                    Id = Guid.NewGuid(),
                    EquipmentId = eq.Id,
                    OccurredAt = DateTime.UtcNow,
                    EventType = "CheckedOut",
                    PreviousStatus = prevStatus,
                    NewStatus = "Unavailable",
                    ProjectId = projectId,
                    TaskId = stage.TaskId,
                    UserId = _auth.UserId,
                    UserName = _auth.UserName,
                    Comment = $"Выдано на этап: {stage.Name}"
                });

                await _sync.QueueOperationAsync("EquipmentHistory", eq.Id, SyncOperation.Create,
                    new RecordEquipmentEventRequest(
                        EventType: EquipmentHistoryEventType.CheckedOut,
                        NewStatus: EquipmentStatus.Unavailable,
                        ProjectId: projectId,
                        TaskId: stage.TaskId,
                        Comment: $"Выдано на этап: {stage.Name}"));
            }
            else
            {
                var stillUsed = await db.StageEquipments
                    .Where(x => x.EquipmentId == eqId && x.StageId != stage.Id)
                    .Join(db.TaskStages, se => se.StageId, st => st.Id, (se, st) => new
                    {
                        st.TaskId,
                        st.Status,
                        st.IsMarkedForDeletion,
                        st.IsArchived
                    })
                    .AnyAsync(x => x.TaskId == stage.TaskId
                                   && x.Status != StageStatus.Completed
                                   && !x.IsMarkedForDeletion
                                   && !x.IsArchived);
                if (stillUsed) continue;
                if (eq.CheckedOutTaskId != stage.TaskId) continue;

                var prevStatus = eq.Status;
                eq.Status = "Available";
                eq.CheckedOutTaskId = null;
                eq.CheckedOutProjectId = null;
                eq.UpdatedAt = DateTime.UtcNow;
                eq.IsSynced = false;

                db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
                {
                    Id = Guid.NewGuid(),
                    EquipmentId = eq.Id,
                    OccurredAt = DateTime.UtcNow,
                    EventType = "Returned",
                    PreviousStatus = prevStatus,
                    NewStatus = "Available",
                    ProjectId = projectId,
                    TaskId = stage.TaskId,
                    UserId = _auth.UserId,
                    UserName = _auth.UserName,
                    Comment = $"Возвращено с этапа: {stage.Name}"
                });

                await _sync.QueueOperationAsync("EquipmentHistory", eq.Id, SyncOperation.Create,
                    new RecordEquipmentEventRequest(
                        EventType: EquipmentHistoryEventType.Returned,
                        NewStatus: EquipmentStatus.Available,
                        ProjectId: projectId,
                        TaskId: stage.TaskId,
                        Comment: $"Возвращено с этапа: {stage.Name}"));
            }
        }
    }

    private static bool ShouldReserveStageEquipment(LocalTaskStage stage) =>
        !stage.IsMarkedForDeletion
        && !stage.IsArchived
        && stage.Status != StageStatus.Completed;

    public async Task EditTaskAsync(Guid taskId, UpdateTaskRequest req)
    {
        if (!DueDatePolicy.IsAllowed(req.DueDate))
            throw new ArgumentException(DueDatePolicy.PastNotAllowedMessage);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(taskId);
        if (task is null) return;

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        task.Name = req.Name;
        task.Description = req.Description;
        task.AssignedUserId = req.AssignedUserId;
        task.AssignedUserName = assignedName;
        task.Priority = req.Priority;
        task.DueDate = req.DueDate;
        // Status is auto-calculated from stages (StatusCalculator)
        var stages = await db.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
        task.TotalStages = stages.Count;
        task.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);
        task.InProgressStages = stages.Count(s => s.Status == StageStatus.InProgress);
        if (stages.Count > 0)
            task.Status = StatusCalculator.GetTaskStatusFromStages(stages);
        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        var syncTaskReq = req with
        {
            IsMarkedForDeletion = task.IsMarkedForDeletion,
            IsArchived = task.IsArchived
        };
        await _sync.QueueOperationAsync("Task", taskId, SyncOperation.Update, syncTaskReq);
        await LogActivityAsync(db, $"Обновлена задача «{req.Name}»", "Task", taskId, ActivityActionKind.Updated);
        await LoadAsync();
    }

    public async Task ChangeTaskStatusAsync(Models.TaskStatus newStatus)
    {
        if (Task is null) return;
        var req = new UpdateTaskRequest(Task.Name, Task.Description, Task.AssignedUserId,
            Task.Priority, Task.DueDate, newStatus, Task.IsMarkedForDeletion, Task.IsArchived);
        await EditTaskAsync(Task.Id, req);
    }

    [RelayCommand]
    private async Task ChangeStageStatusAsync((LocalTaskStage stage, StageStatus newStatus) args)
    {
        var (stage, newStatus) = args;
        if (stage.EffectiveMarkedForDeletion) return;
        if (stage.Status == StageStatus.Completed && newStatus != StageStatus.Completed) return;
        var req = new UpdateStageRequest(stage.Name, stage.Description,
            stage.AssignedUserId, newStatus, stage.DueDate, stage.IsMarkedForDeletion, stage.IsArchived);
        await SaveUpdatedStageAsync(stage.Id, req);
    }

    [RelayCommand]
    private async Task MarkTaskForDeletionAsync()
    {
        if (Task is null || !Task.CanToggleTaskDeletionMark) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Tasks.FindAsync(Task.Id);
        if (entity is null) return;
        var proj = await db.Projects.FindAsync(entity.ProjectId);
        if (proj?.IsMarkedForDeletion == true) return;

        var wasMarked = entity.IsMarkedForDeletion;
        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;

        var stages = await db.TaskStages.Where(s => s.TaskId == Task.Id).ToListAsync();
        if (!entity.IsMarkedForDeletion && wasMarked)
        {
            foreach (var stage in stages)
            {
                stage.IsMarkedForDeletion = false;
                stage.IsSynced = false;
                stage.UpdatedAt = DateTime.UtcNow;
                stage.LastModifiedLocally = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Task", Task.Id, SyncOperation.Update, SyncPayloads.Task(entity));
        var action = entity.IsMarkedForDeletion ? "Помечена для удаления" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: задача «{Task.Name}»", "Task", Task.Id, actionType);

        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkStageForDeletionAsync(LocalTaskStage stage)
    {
        if (!CanMarkStageDeletion()) return;
        if (!stage.CanToggleStageDeletionMark) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;
        var task = await db.Tasks.FindAsync(entity.TaskId);
        var proj = task is not null ? await db.Projects.FindAsync(task.ProjectId) : null;
        if (task?.IsMarkedForDeletion == true || proj?.IsMarkedForDeletion == true)
            return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LastModifiedLocally = DateTime.UtcNow;
        var eqIds = await db.StageEquipments
            .Where(x => x.StageId == stage.Id)
            .Select(x => x.EquipmentId)
            .Distinct()
            .ToListAsync();
        await SetStageEquipmentStateAsync(db, entity, eqIds, reserve: ShouldReserveStageEquipment(entity), task?.ProjectId);
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Update, SyncPayloads.Stage(entity));

        var action = entity.IsMarkedForDeletion ? "Помечен для удаления" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: этап «{stage.Name}»", "Stage", stage.Id, actionType);
        await UpdateTaskProgressAsync();
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteStageAsync(LocalTaskStage stage)
    {
        if (!CanDeleteStage()) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;
        var task = await db.Tasks.FindAsync(entity.TaskId);
        var eqIds = await db.StageEquipments
            .Where(x => x.StageId == stage.Id)
            .Select(x => x.EquipmentId)
            .Distinct()
            .ToListAsync();
        await SetStageEquipmentStateAsync(db, entity, eqIds, reserve: false, task?.ProjectId);

        db.TaskStages.Remove(entity);
        await db.SaveChangesAsync();

        if (stage.IsSynced)
            await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Delete, new { });

        await LogActivityAsync(db, $"Удалён этап «{stage.Name}»", "Stage", stage.Id, ActivityActionKind.Deleted);
        await LoadAsync();
        await UpdateTaskProgressAsync();
    }

    private async System.Threading.Tasks.Task UpdateTaskProgressAsync()
    {
        if (Task is null) return;
        var taskId = Task.Id;
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var taskEntity = await ctx.Tasks.FindAsync(taskId);
        if (taskEntity is null) return;
        var proj = await ctx.Projects.FindAsync(taskEntity.ProjectId);
        taskEntity.ProjectIsMarkedForDeletion = proj?.IsMarkedForDeletion ?? false;
        var stages = await ctx.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
        foreach (var s in stages)
        {
            s.TaskIsMarkedForDeletion = taskEntity.IsMarkedForDeletion;
            s.ProjectIsMarkedForDeletion = taskEntity.ProjectIsMarkedForDeletion;
        }
        ProgressCalculator.ApplyTaskMetrics(taskEntity, stages);

        taskEntity.IsSynced = false;
        taskEntity.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        // Auto-update project status based on task completion
        await RecalcProjectStatusAsync(ctx, taskEntity.ProjectId);

        // Update the in-memory Task so the UI reflects the new progress immediately
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Task is not null)
            {
                Task.TotalStages = taskEntity.TotalStages;
                Task.CompletedStages = taskEntity.CompletedStages;
                Task.InProgressStages = taskEntity.InProgressStages;
                Task.Status = taskEntity.Status;
                OnPropertyChanged(nameof(Task));
            }
        });
    }

    private static async System.Threading.Tasks.Task RecalcProjectStatusAsync(LocalDbContext db, Guid projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && !t.IsMarkedForDeletion).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = taskIds.Count == 0
            ? new List<LocalTaskStage>()
            : await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).ToListAsync();

        foreach (var task in tasks)
        {
            task.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
            var taskStages = stages.Where(s => s.TaskId == task.Id).ToList();
            foreach (var s in taskStages)
            {
                s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
                s.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
            }
            ProgressCalculator.ApplyTaskMetrics(task, taskStages);
        }

        project.Status = StatusCalculator.GetProjectStatusFromTasks(tasks);
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
