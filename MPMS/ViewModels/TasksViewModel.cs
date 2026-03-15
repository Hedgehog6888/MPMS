using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

public partial class TasksViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;

    [ObservableProperty] private ObservableCollection<LocalTask> _tasks = [];
    [ObservableProperty] private ObservableCollection<ProjectTaskGroup> _taskGroups = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _plannedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _inProgressTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _pausedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _completedTasks = [];
    [ObservableProperty] private string _viewMode = "List";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "Все";
    [ObservableProperty] private string _priorityFilter = "Все";
    [ObservableProperty] private Guid? _projectFilter;
    [ObservableProperty] private string _projectFilterName = "Все проекты";
    [ObservableProperty] private ObservableCollection<LocalProject> _projects = [];
    [ObservableProperty] private ObservableCollection<ProjectFilterOption> _projectFilterOptions = [];

    public IReadOnlyList<string> StatusOptions { get; } =
        ["Все", "Запланирована", "Выполняется", "Приостановлена", "Завершена", "Пометка удалить"];

    public IReadOnlyList<string> PriorityOptions { get; } =
        ["Все", "Низкий", "Средний", "Высокий", "Критический"];

    public TasksViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();
    partial void OnPriorityFilterChanged(string value) => _ = LoadAsync();
    partial void OnProjectFilterChanged(Guid? value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var projectList = await db.Projects.Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync();
        Projects = new ObservableCollection<LocalProject>(projectList);
        var filterOpts = new List<ProjectFilterOption> { new(null, "Все проекты") };
        filterOpts.AddRange(projectList.Select(p => new ProjectFilterOption(p.Id, p.Name)));
        ProjectFilterOptions = new ObservableCollection<ProjectFilterOption>(filterOpts);

        var query = db.Tasks.Where(t => !t.IsArchived);

        // Role-based filtering: Workers only see tasks assigned to them
        bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);
        if (isWorker && _auth.UserId.HasValue)
            query = query.Where(t => t.AssignedUserId == _auth.UserId.Value);

        if (ProjectFilter.HasValue)
            query = query.Where(t => t.ProjectId == ProjectFilter.Value);

        if (StatusFilter == "Пометка удалить")
        {
            query = query.Where(t => t.IsMarkedForDeletion);
        }
        else if (StatusFilter != "Все")
        {
            var status = StatusFilter switch
            {
                "Запланирована"    => TaskStatus.Planned,
                "Выполняется"      => TaskStatus.InProgress,
                "Приостановлена"   => TaskStatus.Paused,
                "Завершена"        => TaskStatus.Completed,
                _                  => (TaskStatus?)null
            };
            if (status.HasValue) query = query.Where(t => t.Status == status.Value);
        }

        if (PriorityFilter != "Все")
        {
            var priority = PriorityFilter switch
            {
                "Низкий"        => TaskPriority.Low,
                "Средний"       => TaskPriority.Medium,
                "Высокий"       => TaskPriority.High,
                "Критический"   => TaskPriority.Critical,
                _               => (TaskPriority?)null
            };
            if (priority.HasValue) query = query.Where(t => t.Priority == priority.Value);
        }

        var list = await query.ToListAsync();

        // Load stages and recalculate task status from stages (same as ProjectDetailViewModel)
        var taskIds = list.Select(t => t.Id).ToList();
        var stages = await db.TaskStages
            .Where(s => taskIds.Contains(s.TaskId))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        foreach (var t in list)
        {
            var taskStages = stages.Where(s => s.TaskId == t.Id).ToList();
            t.TotalStages = taskStages.Count;
            t.CompletedStages = taskStages.Count(s => s.Status == StageStatus.Completed);
            t.InProgressStages = taskStages.Count(s => s.Status == StageStatus.InProgress);
            if (taskStages.Count > 0)
                t.Status = StatusCalculator.GetTaskStatusFromStages(taskStages);
        }

        var searchTerm = SearchHelper.Normalize(SearchText);
        if (searchTerm is not null)
            list = list.Where(t => SearchHelper.ContainsIgnoreCase(t.Name, searchTerm) ||
                SearchHelper.ContainsIgnoreCase(t.Description, searchTerm)).ToList();

        list = list
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
            .ThenByDescending(t => t.CreatedAt)
            .ToList();
        Tasks = new ObservableCollection<LocalTask>(list);

        static int StatusOrder(TaskStatus s) => s switch
        {
            TaskStatus.Planned => 0, TaskStatus.InProgress => 1,
            TaskStatus.Paused => 2, TaskStatus.Completed => 3, _ => 4
        };
        var groups = list
            .GroupBy(t => new { t.ProjectId, t.ProjectName })
            .OrderBy(g => g.Key.ProjectName)
            .Select(g => new ProjectTaskGroup(g.Key.ProjectId, g.Key.ProjectName ?? "—",
                g.OrderBy(t => t.IsMarkedForDeletion).ThenBy(t => StatusOrder(t.Status))
                    .ThenBy(t => t.ProgressPercent).ThenByDescending(t => t.CreatedAt).ToList()))
            .ToList();
        TaskGroups = new ObservableCollection<ProjectTaskGroup>(groups);

        PlannedTasks    = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.Planned    && !t.IsMarkedForDeletion));
        InProgressTasks = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.InProgress && !t.IsMarkedForDeletion));
        PausedTasks     = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.Paused     && !t.IsMarkedForDeletion));
        CompletedTasks  = new ObservableCollection<LocalTask>(list.Where(t => t.Status == TaskStatus.Completed  && !t.IsMarkedForDeletion));
    }

    public void SetProjectFilter(LocalProject? project)
    {
        ProjectFilter = project?.Id;
        ProjectFilterName = project?.Name ?? "Все проекты";
    }

    public async Task<LocalProject?> GetProjectForTaskAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return null;

        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && !t.IsMarkedForDeletion && !t.IsArchived).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived).ToListAsync();

        foreach (var t in tasks)
        {
            var taskStages = stages.Where(s => s.TaskId == t.Id).ToList();
            t.TotalStages = taskStages.Count;
            t.CompletedStages = taskStages.Count(s => s.Status == StageStatus.Completed);
            t.InProgressStages = taskStages.Count(s => s.Status == StageStatus.InProgress);
            if (taskStages.Count > 0)
                t.Status = StatusCalculator.GetTaskStatusFromStages(taskStages);
        }

        project.TotalTasks = tasks.Count;
        project.CompletedTasks = tasks.Count(t => t.Status == TaskStatus.Completed);
        project.InProgressTasks = tasks.Count(t => t.Status == TaskStatus.InProgress);
        project.Status = StatusCalculator.GetProjectStatusFromTasks(tasks);
        return project;
    }

    public async Task SaveNewTaskAsync(CreateTaskRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var projectName = await db.Projects
            .Where(p => p.Id == req.ProjectId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync() ?? "—";

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        var task = new LocalTask
        {
            Id = localId,
            ProjectId = req.ProjectId,
            ProjectName = projectName,
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

        await RecalcProjectStatusAsync(db, req.ProjectId);
        await LoadAsync();
    }

    private static async Task RecalcProjectStatusAsync(LocalDbContext db, Guid projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && !t.IsMarkedForDeletion && !t.IsArchived).ToListAsync();
        project.Status = StatusCalculator.GetProjectStatusFromTasks(tasks);
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
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
        task.Status = req.Status;
        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Task", id, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлена задача «{req.Name}»", "Task", id, ActivityActionKind.Updated);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MoveTaskAsync((LocalTask task, Models.TaskStatus newStatus) args)
    {
        // Статус задачи вычисляется автоматически из этапов — перетаскивание в Kanban игнорируется
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(LocalTask task)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Tasks.FindAsync(task.Id);
        if (entity is null) return;

        entity.IsArchived = true;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;

        var stages = await db.TaskStages.Where(s => s.TaskId == task.Id).ToListAsync();
        foreach (var s in stages)
        {
            s.IsArchived = true;
            s.IsSynced = false;
            s.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

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
}

public class ProjectTaskGroup
{
    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public List<LocalTask> Tasks { get; }

    public ProjectTaskGroup(Guid projectId, string projectName, List<LocalTask> tasks)
    {
        ProjectId = projectId;
        ProjectName = projectName;
        Tasks = tasks;
    }
}

public class ProjectFilterOption
{
    public Guid? Id { get; }
    public string Name { get; }

    public ProjectFilterOption(Guid? id, string name)
    {
        Id = id;
        Name = name;
    }
}
