using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class StageItem : ObservableObject
{
    public LocalTaskStage Stage { get; init; } = null!;
    public string TaskName    { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public Guid   TaskId      { get; init; }
    public Guid   ProjectId   { get; init; }

    public bool CanDragInKanban => !Stage.EffectiveMarkedForDeletion;
}

public partial class StagesViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;

    [ObservableProperty] private ObservableCollection<StageItem> _stages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredStages = [];
    [ObservableProperty] private ObservableCollection<TaskStageGroup> _stageGroups = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "Все статусы";

    // Фильтры по проектам и задачам
    [ObservableProperty] private Guid? _projectFilter;
    [ObservableProperty] private Guid? _taskFilter;

    [ObservableProperty] private ObservableCollection<ProjectFilterOption> _projectFilterOptions = [];
    [ObservableProperty] private ObservableCollection<TaskFilterOption> _taskFilterOptions = [];

    public List<string> StatusOptions { get; } =
        ["Все статусы", "Запланирован", "Выполняется", "Завершён", "Пометка удалить"];

    public StagesViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();
    partial void OnProjectFilterChanged(Guid? value)
    {
        UpdateTaskFilterOptions();
        ApplyFilter();
    }

    partial void OnTaskFilterChanged(Guid? value) => ApplyFilter();

    public async Task LoadAsync()
    {
        IsBusy = true;
        ClearMessages();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var tasksQuery = db.Tasks.Where(t => !t.IsArchived);
            var userId = _auth.UserId;
            bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
            bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
            bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

            if (userId.HasValue)
            {
                if (isManager)
                    tasksQuery = tasksQuery.Where(t => db.Projects.Any(p => p.Id == t.ProjectId && p.ManagerId == userId.Value));
                else if (isForeman)
                {
                    var foremanProjectIds = await db.ProjectMembers
                        .Where(m => m.UserId == userId.Value)
                        .Select(m => m.ProjectId)
                        .ToListAsync();
                    tasksQuery = tasksQuery.Where(t => foremanProjectIds.Contains(t.ProjectId));
                }
                else if (isWorker)
                {
                    var workerTaskIds = await db.Tasks
                        .Where(t => t.AssignedUserId == userId.Value)
                        .Select(t => t.Id)
                        .ToListAsync();
                    var workerTaskIdsFromAssignees = await db.TaskAssignees
                        .Where(ta => ta.UserId == userId.Value)
                        .Select(ta => ta.TaskId)
                        .ToListAsync();
                    var workerStageIds = await db.StageAssignees
                        .Where(sa => sa.UserId == userId.Value)
                        .Select(sa => sa.StageId)
                        .ToListAsync();
                    var workerStageIdsFromAssigned = await db.TaskStages
                        .Where(s => s.AssignedUserId == userId.Value)
                        .Select(s => s.Id)
                        .ToListAsync();
                    var allWorkerTaskIds = workerTaskIds.Concat(workerTaskIdsFromAssignees).Distinct().ToList();
                    var allWorkerStageIds = workerStageIds.Concat(workerStageIdsFromAssigned).Distinct().ToList();
                    tasksQuery = tasksQuery.Where(t =>
                        allWorkerTaskIds.Contains(t.Id) ||
                        db.TaskStages.Any(s => s.TaskId == t.Id && allWorkerStageIds.Contains(s.Id)));
                }
            }

            var tasks = await tasksQuery.ToListAsync();
            var taskDict = tasks.ToDictionary(t => t.Id);
            var taskIds = tasks.Select(t => t.Id).ToList();

            var stageList = await db.TaskStages
                .Where(s => !s.IsArchived && taskIds.Contains(s.TaskId))
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            if (userId.HasValue && isWorker)
            {
                var workerStageIds = await db.StageAssignees
                    .Where(sa => sa.UserId == userId.Value)
                    .Select(sa => sa.StageId)
                    .ToListAsync();
                var workerStageIdsFromAssigned = await db.TaskStages
                    .Where(s => s.AssignedUserId == userId.Value)
                    .Select(s => s.Id)
                    .ToListAsync();
                var allWorkerStageIds = workerStageIds.Concat(workerStageIdsFromAssigned).Distinct().ToList();
                stageList = stageList.Where(s => allWorkerStageIds.Contains(s.Id)).ToList();
            }

            var assigneeIds = stageList
                .Where(s => s.AssignedUserId.HasValue)
                .Select(s => s.AssignedUserId!.Value)
                .Distinct()
                .ToList();
            if (assigneeIds.Count > 0)
            {
                var avatars = await db.Users
                    .Where(u => assigneeIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                    .ToDictionaryAsync(u => u.Id);

                foreach (var stage in stageList)
                {
                    if (stage.AssignedUserId.HasValue &&
                        avatars.TryGetValue(stage.AssignedUserId.Value, out var avatar))
                    {
                        stage.AssignedUserAvatarData = avatar.AvatarData;
                        stage.AssignedUserAvatarPath = avatar.AvatarPath;
                    }
                }
            }

            var projectIds = tasks.Select(t => t.ProjectId).Distinct().ToList();
            var projMarked = projectIds.Count == 0
                ? new Dictionary<Guid, bool>()
                : await db.Projects.Where(p => projectIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.IsMarkedForDeletion })
                    .ToDictionaryAsync(x => x.Id, x => x.IsMarkedForDeletion);
            foreach (var s in stageList)
            {
                taskDict.TryGetValue(s.TaskId, out var tk);
                s.TaskIsMarkedForDeletion = tk?.IsMarkedForDeletion ?? false;
                var pid = tk?.ProjectId ?? Guid.Empty;
                s.ProjectIsMarkedForDeletion = projMarked.GetValueOrDefault(pid);
            }

            var items = stageList.Select(s =>
            {
                taskDict.TryGetValue(s.TaskId, out var task);
                return new StageItem
                {
                    Stage       = s,
                    TaskId      = s.TaskId,
                    TaskName    = task?.Name    ?? "—",
                    ProjectId   = task?.ProjectId ?? Guid.Empty,
                    ProjectName = task?.ProjectName ?? "—"
                };
            }).ToList();

            Stages = new ObservableCollection<StageItem>(items);

            // Построение опций фильтров по проектам и задачам
            var projectOpts = new List<ProjectFilterOption> { new(null, "Все проекты") };
            projectOpts.AddRange(items
                .Where(i => i.ProjectId != Guid.Empty)
                .GroupBy(i => new { i.ProjectId, i.ProjectName })
                .OrderBy(g => g.Key.ProjectName)
                .Select(g => new ProjectFilterOption(g.Key.ProjectId, g.Key.ProjectName)));
            ProjectFilterOptions = new ObservableCollection<ProjectFilterOption>(projectOpts);

            UpdateTaskFilterOptions();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = Stages.AsEnumerable();

        if (SearchHelper.Normalize(SearchText) is { } term)
        {
            query = query.Where(s =>
                SearchHelper.ContainsIgnoreCase(s.Stage.Name, term) ||
                SearchHelper.ContainsIgnoreCase(s.TaskName, term) ||
                SearchHelper.ContainsIgnoreCase(s.ProjectName, term));
        }

        if (ProjectFilter.HasValue)
            query = query.Where(s => s.ProjectId == ProjectFilter.Value);

        if (TaskFilter.HasValue)
            query = query.Where(s => s.TaskId == TaskFilter.Value);

        if (StatusFilter == "Пометка удалить")
        {
            query = query.Where(s => s.Stage.EffectiveMarkedForDeletion);
        }
        else if (StatusFilter != "Все статусы")
        {
            var targetStatus = StatusFilter switch
            {
                "Запланирован" => StageStatus.Planned,
                "Выполняется"  => StageStatus.InProgress,
                "Завершён"     => StageStatus.Completed,
                _              => (StageStatus?)null
            };
            if (targetStatus.HasValue)
                query = query.Where(s => s.Stage.Status == targetStatus.Value);
        }

        FilteredStages = new ObservableCollection<StageItem>(query);

        var groups = query
            .GroupBy(s => new { s.TaskId, s.TaskName, s.ProjectId, s.ProjectName })
            .OrderBy(g => g.Key.ProjectName)
            .ThenBy(g => g.Key.TaskName)
            .Select(g => new TaskStageGroup(g.Key.TaskId, g.Key.TaskName, g.Key.ProjectId, g.Key.ProjectName,
                g.OrderBy(s => s.Stage.EffectiveMarkedForDeletion).ToList()))
            .ToList();
        StageGroups = new ObservableCollection<TaskStageGroup>(groups);
    }

    private void UpdateTaskFilterOptions()
    {
        IEnumerable<StageItem> source = Stages;

        if (ProjectFilter.HasValue)
            source = source.Where(i => i.ProjectId == ProjectFilter.Value);

        var taskOpts = new List<TaskFilterOption> { new(null, "Все задачи") };
        taskOpts.AddRange(source
            .Where(i => i.TaskId != Guid.Empty)
            .GroupBy(i => new { i.TaskId, i.TaskName })
            .OrderBy(g => g.Key.TaskName)
            .Select(g => new TaskFilterOption(g.Key.TaskId, g.Key.TaskName)));

        TaskFilterOptions = new ObservableCollection<TaskFilterOption>(taskOpts);

        // Сбросить текущий фильтр задачи, если он больше не доступен
        if (!TaskFilterOptions.Any(o => o.Id == TaskFilter))
            TaskFilter = null;
    }

    [RelayCommand]
    private async Task MarkStageForDeletionAsync(StageItem item)
    {
        if (!item.Stage.CanToggleStageDeletionMark) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(item.Stage.Id);
        if (entity is null) return;
        var task = await db.Tasks.FindAsync(entity.TaskId);
        var proj = task is not null ? await db.Projects.FindAsync(task.ProjectId) : null;
        if (task?.IsMarkedForDeletion == true || proj?.IsMarkedForDeletion == true)
            return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var action = entity.IsMarkedForDeletion ? "Помечен к удалению" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: этап «{item.Stage.Name}»", "Stage", item.Stage.Id, actionType);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteStageAsync(StageItem item)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(item.Stage.Id);
        if (entity is null) return;

        entity.IsArchived = true;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await LogActivityAsync(db, $"Удалён этап «{item.Stage.Name}»", "Stage", item.Stage.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    private static async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId, string? actionType = null)
    {
        var session = await db.AuthSessions.FindAsync(1);
        var userName = session?.UserName ?? "Система";
        var userId = session?.UserId;
        var actorRole = session?.UserRole;
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : userName.Length > 0 ? $"{userName[0]}" : "?";

        db.ActivityLogs.Add(new LocalActivityLog
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
        });
        await db.SaveChangesAsync();
    }

    [RelayCommand]
    private async Task ChangeStageStatusAsync((StageItem item, StageStatus newStatus) args)
    {
        var (item, newStatus) = args;
        if (item.Stage.EffectiveMarkedForDeletion) return;
        var vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        var task = await GetTaskForStageAsync(item.TaskId);
        if (task is null) return;
        vm.SetTask(task);
        var req = new UpdateStageRequest(item.Stage.Name, item.Stage.Description,
            item.Stage.AssignedUserId, newStatus);
        await vm.SaveUpdatedStageAsync(item.Stage.Id, req);
        await LoadAsync();
    }

    public async Task<LocalTask?> GetTaskForStageAsync(Guid taskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks.FindAsync(taskId);
    }
}

public class TaskStageGroup
{
    public Guid TaskId { get; }
    public string TaskName { get; }
    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public List<StageItem> Stages { get; }

    public TaskStageGroup(Guid taskId, string taskName, Guid projectId, string projectName, List<StageItem> stages)
    {
        TaskId = taskId;
        TaskName = taskName;
        ProjectId = projectId;
        ProjectName = projectName;
        Stages = stages;
    }
}

public class TaskFilterOption
{
    public Guid? Id { get; }
    public string Name { get; }

    public TaskFilterOption(Guid? id, string name)
    {
        Id = id;
        Name = name;
    }
}
