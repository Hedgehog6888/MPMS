using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Controls;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

public partial class ProjectDetailViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private Action? _goBackAction;

    [ObservableProperty] private LocalProject? _project;

    // ─── Tasks collections ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LocalTask> _tasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _plannedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _inProgressTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _pausedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _completedTasks = [];

    // Filtered views for UI (list + kanban)
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredPlannedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredInProgressTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredPausedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredCompletedTasks = [];

    // ─── Task filters ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _taskSearchText = string.Empty;
    [ObservableProperty] private string _taskStatusFilter = "Все";
    [ObservableProperty] private string _taskPriorityFilter = "Все";

    public IReadOnlyList<string> TaskStatusOptions { get; } =
        ["Все", "Запланирована", "Выполняется", "Приостановлена", "Завершена", "Пометка удалить"];

    public IReadOnlyList<string> TaskPriorityOptions { get; } =
        ["Все", "Низкий", "Средний", "Высокий", "Критический"];

    // ─── Stages collections & filters ─────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LocalTaskStage> _allStages = [];
    [ObservableProperty] private ObservableCollection<LocalTaskStage> _filteredStages = [];
    [ObservableProperty] private string _stageSearchText = string.Empty;
    [ObservableProperty] private string _stageStatusFilter = "Все статусы";

    // Фильтр этапов по задаче внутри проекта
    [ObservableProperty] private Guid? _stageTaskFilter;
    [ObservableProperty] private ObservableCollection<TaskFilterOption> _stageTaskFilterOptions = [];

    public List<string> StageStatusOptions { get; } =
        ["Все статусы", "Запланирован", "Выполняется", "Завершён", "Пометка удалить"];

    // ─── UI state and other entities ──────────────────────────────────────────
    [ObservableProperty] private string _activeTab = "Tasks";
    [ObservableProperty] private string _taskViewMode = "List";
    [ObservableProperty] private ObservableCollection<LocalFile> _files = [];
    [ObservableProperty] private ObservableCollection<LocalProjectMember> _members = [];

    [ObservableProperty] private List<LocalProjectMember> _foremanMembers = [];
    [ObservableProperty] private List<LocalProjectMember> _workerMembers = [];
    [ObservableProperty] private int _totalTasks;
    [ObservableProperty] private int _completedTasksCount;
    [ObservableProperty] private int _inProgressTasksCount;
    [ObservableProperty] private int _overdueTasksCount;
    [ObservableProperty] private int _projectProgressPercent;
    [ObservableProperty] private IList<DonutSegment> _taskStatsSegments = [];
    [ObservableProperty] private ObservableCollection<LocalMessage> _messages = [];

    public ProjectDetailViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    // ─── Filter change handlers ────────────────────────────────────────────────
    partial void OnTaskSearchTextChanged(string value) => ApplyTaskFilter();
    partial void OnTaskStatusFilterChanged(string value) => ApplyTaskFilter();
    partial void OnTaskPriorityFilterChanged(string value) => ApplyTaskFilter();

    partial void OnStageSearchTextChanged(string value) => ApplyStageFilter();
    partial void OnStageStatusFilterChanged(string value) => ApplyStageFilter();
    partial void OnStageTaskFilterChanged(Guid? value) => ApplyStageFilter();

    public void SetProject(LocalProject project, Action? goBackAction = null)
    {
        Project = project;
        _goBackAction = goBackAction;
    }

    public async Task LoadAsync()
    {
        if (Project is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Reload project from DB to get latest IsMarkedForDeletion and Status
        var projectEntity = await db.Projects.FindAsync(Project.Id);
        if (projectEntity is not null)
        {
            Project = projectEntity;
        }

        var tasksQuery = db.Tasks.Where(t => t.ProjectId == Project.Id);

        // Workers only see tasks assigned to them
        bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);
        if (isWorker && _auth.UserId.HasValue)
            tasksQuery = tasksQuery.Where(t => t.AssignedUserId == _auth.UserId.Value);

        var tasks = await tasksQuery
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Load stages and compute TotalStages/CompletedStages + auto task status for each task
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages
            .Where(s => taskIds.Contains(s.TaskId))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        foreach (var task in tasks)
        {
            var taskStages = stages.Where(s => s.TaskId == task.Id).ToList();
            task.TotalStages = taskStages.Count;
            task.CompletedStages = taskStages.Count(s => s.Status == StageStatus.Completed);
            task.InProgressStages = taskStages.Count(s => s.Status == StageStatus.InProgress);
            // Статус задачи: завершённый + планируемый = в работе (StatusCalculator)
            if (taskStages.Count > 0)
                task.Status = StatusCalculator.GetTaskStatusFromStages(taskStages);
        }

        // Persist task status changes and recalc project status
        await RecalcAndSaveTaskStatusesAsync(db, tasks);
        await RecalcProjectStatusAsync(db);

        Tasks = new ObservableCollection<LocalTask>(tasks);
        PlannedTasks    = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.Planned));
        InProgressTasks = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.InProgress));
        PausedTasks     = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.Paused));
        CompletedTasks  = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.Completed));

        // Initialize filtered task collections based on current filters
        ApplyTaskFilter();

        // Progress: exclude tasks marked for deletion. ProgressCalculator — единая формула с ProjectsViewModel
        var activeTasks = tasks.Where(t => !t.IsMarkedForDeletion).ToList();
        TotalTasks = activeTasks.Count;
        CompletedTasksCount = activeTasks.Count(t => t.Status == TaskStatus.Completed);
        InProgressTasksCount = activeTasks.Count(t => t.Status == TaskStatus.InProgress);
        OverdueTasksCount = tasks.Count(t => t.IsOverdue);
        ProjectProgressPercent = (int)Math.Round(ProgressCalculator.GetProjectProgressPercent(CompletedTasksCount, InProgressTasksCount, TotalTasks));
        if (Project is not null)
        {
            Project.TotalTasks = TotalTasks;
            Project.CompletedTasks = CompletedTasksCount;
            Project.InProgressTasks = InProgressTasksCount;
        }

        int plannedCount = activeTasks.Count(t => t.Status == TaskStatus.Planned);
        TaskStatsSegments = new List<DonutSegment>
        {
            new() { Label = "Завершено",    Value = CompletedTasksCount,  Color = Color.FromRgb(0x22, 0xC5, 0x5E) },
            new() { Label = "В процессе",   Value = InProgressTasksCount, Color = Color.FromRgb(0xEA, 0xB3, 0x08) },
            new() { Label = "Просрочено",   Value = OverdueTasksCount,    Color = Color.FromRgb(0xEF, 0x44, 0x44) },
            new() { Label = "Запланировано",Value = plannedCount,          Color = Color.FromRgb(0x3B, 0x82, 0xF6) },
        };

        // Populate TaskName for each stage (stages already loaded above)
        var taskNameDict = tasks.ToDictionary(t => t.Id, t => t.Name);
        foreach (var stage in stages)
            stage.TaskName = taskNameDict.GetValueOrDefault(stage.TaskId, "—");

        AllStages = new ObservableCollection<LocalTaskStage>(stages);

        // Построить опции фильтра задач для вкладки "Этапы" проекта
        var taskOpts = new List<TaskFilterOption> { new(null, "Все задачи") };
        taskOpts.AddRange(stages
            .Where(s => s.TaskId != Guid.Empty)
            .GroupBy(s => new { s.TaskId, s.TaskName })
            .OrderBy(g => g.Key.TaskName)
            .Select(g => new TaskFilterOption(g.Key.TaskId, g.Key.TaskName ?? "—")));
        StageTaskFilterOptions = new ObservableCollection<TaskFilterOption>(taskOpts);

        // Initialize filtered stages based on current filters
        ApplyStageFilter();

        // Load files
        var files = await db.Files
            .Where(f => f.ProjectId == Project.Id)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
        Files = new ObservableCollection<LocalFile>(files);

        // Load project members (executors) with AvatarPath from Users
        var members = await db.ProjectMembers
            .Where(m => m.ProjectId == Project.Id)
            .OrderBy(m => m.UserName)
            .ToListAsync();
        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.AvatarPath);
        foreach (var m in members)
            m.AvatarPath = userAvatars.GetValueOrDefault(m.UserId);
        Members = new ObservableCollection<LocalProjectMember>(members);
        ForemanMembers = [.. members.Where(m => m.UserRole is "Foreman" or "Прораб")];
        WorkerMembers  = [.. members.Where(m => m.UserRole is "Worker" or "Работник")];

        // Load project messages (discussion)
        var messages = await db.Messages
            .Where(m => m.ProjectId == Project.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        Messages = new ObservableCollection<LocalMessage>(messages);
    }

    private void ApplyTaskFilter()
    {
        var query = Tasks.AsEnumerable();

        if (SearchHelper.Normalize(TaskSearchText) is { } taskTerm)
            query = query.Where(t =>
                SearchHelper.ContainsIgnoreCase(t.Name, taskTerm) ||
                SearchHelper.ContainsIgnoreCase(t.Description, taskTerm));

        if (TaskStatusFilter == "Пометка удалить")
        {
            query = query.Where(t => t.IsMarkedForDeletion);
        }
        else if (TaskStatusFilter != "Все")
        {
            var status = TaskStatusFilter switch
            {
                "Запланирована"  => TaskStatus.Planned,
                "Выполняется"    => TaskStatus.InProgress,
                "Приостановлена" => TaskStatus.Paused,
                "Завершена"      => TaskStatus.Completed,
                _                => (TaskStatus?)null
            };
            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value);
        }

        if (TaskPriorityFilter != "Все")
        {
            var priority = TaskPriorityFilter switch
            {
                "Низкий"      => TaskPriority.Low,
                "Средний"     => TaskPriority.Medium,
                "Высокий"     => TaskPriority.High,
                "Критический" => TaskPriority.Critical,
                _             => (TaskPriority?)null
            };
            if (priority.HasValue)
                query = query.Where(t => t.Priority == priority.Value);
        }

        // Tasks sorted: non-deleted first by status, then by progress desc, then priority, name
        var list = query
            .OrderBy(t => t.IsMarkedForDeletion)
            .ThenBy(t => t.Status switch
            {
                TaskStatus.Planned    => 0,
                TaskStatus.InProgress => 1,
                TaskStatus.Paused     => 2,
                TaskStatus.Completed  => 3,
                _                     => 4
            })
            .ThenBy(t => t.ProgressPercent)
            .ThenByDescending(t => (int)t.Priority)
            .ThenBy(t => t.Name)
            .ToList();

        FilteredTasks           = new ObservableCollection<LocalTask>(list);
        FilteredPlannedTasks    = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.Planned));
        FilteredInProgressTasks = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.InProgress));
        FilteredPausedTasks     = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.Paused));
        FilteredCompletedTasks  = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.Completed));
    }

    private void ApplyStageFilter()
    {
        var query = AllStages.AsEnumerable();

        if (SearchHelper.Normalize(StageSearchText) is { } stageTerm)
            query = query.Where(s =>
                SearchHelper.ContainsIgnoreCase(s.Name, stageTerm) ||
                SearchHelper.ContainsIgnoreCase(s.TaskName, stageTerm));

        if (StageTaskFilter.HasValue)
            query = query.Where(s => s.TaskId == StageTaskFilter.Value);

        if (StageStatusFilter == "Пометка удалить")
        {
            query = query.Where(s => s.IsMarkedForDeletion);
        }
        else if (StageStatusFilter != "Все статусы")
        {
            var targetStatus = StageStatusFilter switch
            {
                "Запланирован" => StageStatus.Planned,
                "Выполняется"  => StageStatus.InProgress,
                "Завершён"     => StageStatus.Completed,
                _              => (StageStatus?)null
            };
            if (targetStatus.HasValue)
                query = query.Where(s => s.Status == targetStatus.Value);
        }

        var list = query.OrderBy(s => s.IsMarkedForDeletion).ToList();
        FilteredStages = new ObservableCollection<LocalTaskStage>(list);
    }

    public async Task UpdateProjectAsync(Guid id, UpdateProjectRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(id);
        if (project is null) return;

        var managerName = await db.Users
            .Where(u => u.Id == req.ManagerId)
            .Select(u => u.Name).FirstOrDefaultAsync() ?? project.ManagerName;

        project.Name = req.Name;
        project.Description = req.Description;
        project.Client = req.Client;
        project.Address = req.Address;
        project.StartDate = req.StartDate;
        project.EndDate = req.EndDate;
        // Status is auto-calculated, do not override from request
        project.ManagerId = req.ManagerId;
        project.ManagerName = managerName;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await RecalcProjectStatusAsync(db);
        await _sync.QueueOperationAsync("Project", id, SyncOperation.Update, req);

        Project = project;
        await LoadAsync();
    }

    [RelayCommand]
    private void GoBack() => _goBackAction?.Invoke();

    [RelayCommand]
    private void SwitchTab(string tab) => ActiveTab = tab;

    [RelayCommand]
    private void SwitchTaskView(string mode) => TaskViewMode = mode;

    [RelayCommand]
    private async Task MarkStageForDeletionAsync(LocalTaskStage stage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;
        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var action = entity.IsMarkedForDeletion ? "Помечен для удаления" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: этап «{stage.Name}»", "Stage", stage.Id, actionType);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteStageAsync(LocalTaskStage stage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;
        db.TaskStages.Remove(entity);
        await db.SaveChangesAsync();
        if (stage.IsSynced)
            await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Delete, new { });
        await LogActivityAsync(db, $"Удалён этап «{stage.Name}»", "Stage", stage.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ChangeStageStatusAsync((LocalTaskStage stage, StageStatus newStatus) args)
    {
        var (stage, newStatus) = args;
        var req = new UpdateStageRequest(stage.Name, stage.Description, stage.AssignedUserId, newStatus);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;
        entity.Status = newStatus;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлён этап «{stage.Name}»", "Stage", stage.Id, ActivityActionKind.Updated);
        await RecalcProjectStatusAsync(db);
        await LoadAsync();
    }

    public async Task SaveNewTaskAsync(CreateTaskRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        var task = new LocalTask
        {
            Id = localId,
            ProjectId = req.ProjectId,
            ProjectName = Project?.Name ?? "—",
            Name = req.Name,
            Description = req.Description,
            AssignedUserId = req.AssignedUserId,
            AssignedUserName = assignedName,
            Priority = req.Priority,
            DueDate = req.DueDate,
            Status = TaskStatus.Planned,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Task", localId, SyncOperation.Create,
            req with { Id = localId });

        await RecalcProjectStatusAsync(db);
        await LogActivityAsync(db, $"Создана задача «{req.Name}»", "Task", localId, ActivityActionKind.Created);
        await LoadAsync();
    }

    public async Task SaveUpdatedTaskAsync(Guid id, UpdateTaskRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(id);
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
        // Status is auto-calculated from stages, do not set from request
        var stages = await db.TaskStages.Where(s => s.TaskId == id).ToListAsync();
        task.TotalStages = stages.Count;
        task.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);
        task.InProgressStages = stages.Count(s => s.Status == StageStatus.InProgress);
        if (stages.Count > 0)
            task.Status = StatusCalculator.GetTaskStatusFromStages(stages);
        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await RecalcProjectStatusAsync(db);
        await _sync.QueueOperationAsync("Task", id, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлена задача «{req.Name}»", "Task", id, ActivityActionKind.Updated);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MoveTaskAsync((LocalTask task, Models.TaskStatus newStatus) args)
    {
        // Task status is auto-calculated from stages — Kanban drag is disabled
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(LocalTask task)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Tasks.FindAsync(task.Id);
        if (entity is null) return;

        // Cascade delete stages associated with this task
        var stages = await db.TaskStages.Where(s => s.TaskId == task.Id).ToListAsync();
        db.TaskStages.RemoveRange(stages);
        db.Tasks.Remove(entity);
        await db.SaveChangesAsync();

        if (task.IsSynced)
            await _sync.QueueOperationAsync("Task", task.Id, SyncOperation.Delete, new { });

        await RecalcProjectStatusAsync(db);
        await LogActivityAsync(db, $"Удалена задача «{task.Name}»", "Task", task.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkTaskForDeletionAsync(LocalTask task)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Tasks.FindAsync(task.Id);
        if (entity is null) return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;

        // Cascade mark/unmark to all stages of this task
        var stages = await db.TaskStages.Where(s => s.TaskId == task.Id).ToListAsync();
        foreach (var stage in stages)
        {
            stage.IsMarkedForDeletion = entity.IsMarkedForDeletion;
            stage.IsSynced = false;
            stage.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        var action = entity.IsMarkedForDeletion ? "Помечена для удаления" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: задача «{task.Name}»", "Task", task.Id, actionType);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkProjectForDeletionAsync()
    {
        if (Project is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(Project.Id);
        if (project is null) return;

        project.IsMarkedForDeletion = !project.IsMarkedForDeletion;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;

        // Cascade mark/unmark to all tasks and their stages
        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).ToListAsync();

        foreach (var t in tasks)
        {
            t.IsMarkedForDeletion = project.IsMarkedForDeletion;
            t.IsSynced = false;
            t.UpdatedAt = DateTime.UtcNow;
        }
        foreach (var s in stages)
        {
            s.IsMarkedForDeletion = project.IsMarkedForDeletion;
            s.IsSynced = false;
            s.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        Project.IsMarkedForDeletion = project.IsMarkedForDeletion;
        var action = project.IsMarkedForDeletion ? "Помечен для удаления" : "Снята пометка удаления";
        var actionType = project.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: проект «{project.Name}»", "Project", project.Id, actionType);
        await LoadAsync();
    }

    public async Task SendMessageAsync(string text)
    {
        if (Project is null || string.IsNullOrWhiteSpace(text)) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var userName = _auth.UserName ?? "—";
        var initials = string.IsNullOrEmpty(userName) ? "?"
            : string.Concat(userName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpperInvariant() : ""));
        if (string.IsNullOrEmpty(initials)) initials = "?";

        var msg = new LocalMessage
        {
            ProjectId = Project.Id,
            UserId = _auth.UserId ?? Guid.Empty,
            UserName = userName,
            UserInitials = initials,
            UserColor = "#1B6EC2",
            UserRole = RoleToRussian(_auth.UserRole),
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        await LogActivityAsync(db, $"Сообщение в обсуждении проекта «{Project.Name}»", "Message", msg.Id, ActivityActionKind.Message);
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

    /// <summary>Persists task status/stages and recalculates project status.</summary>
    private async Task RecalcAndSaveTaskStatusesAsync(LocalDbContext db, List<LocalTask> tasks)
    {
        foreach (var t in tasks)
        {
            var entity = await db.Tasks.FindAsync(t.Id);
            if (entity is null) continue;
            entity.TotalStages = t.TotalStages;
            entity.CompletedStages = t.CompletedStages;
            entity.Status = t.Status;
            entity.IsSynced = false;
            entity.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Recalculates and saves the project's status based on its tasks. Excludes tasks marked for deletion. StatusCalculator.</summary>
    private async Task RecalcProjectStatusAsync(LocalDbContext db)
    {
        if (Project is null) return;
        var project = await db.Projects.FindAsync(Project.Id);
        if (project is null) return;

        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id && !t.IsMarkedForDeletion).ToListAsync();
        project.Status = StatusCalculator.GetProjectStatusFromTasks(tasks);

        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (Project is not null)
            Project.Status = project.Status;
    }

    public static string RoleToRussian(string? role) => role switch
    {
        "Administrator" or "Admin" => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager" => "Менеджер",
        "Foreman" => "Прораб",
        "Worker" => "Работник",
        _ => role ?? "—"
    };
}
