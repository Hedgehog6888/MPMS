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
    private bool _suppressProjectFilterReload;

    [ObservableProperty] private ObservableCollection<LocalTask> _tasks = [];
    [ObservableProperty] private ObservableCollection<ProjectTaskGroup> _taskGroups = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "Все";
    [ObservableProperty] private string _priorityFilter = "Все";
    [ObservableProperty] private Guid? _projectFilter;
    [ObservableProperty] private string _projectFilterName = "Все проекты";
    [ObservableProperty] private ObservableCollection<LocalProject> _projects = [];
    [ObservableProperty] private ObservableCollection<ProjectFilterOption> _projectFilterOptions = [];

    public IReadOnlyList<string> StatusOptions { get; } =
        ["Все", "Запланирована", "Выполняется", "Завершена", "Пометка удалить"];

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
    partial void OnProjectFilterChanged(Guid? value)
    {
        if (_suppressProjectFilterReload) return;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var projectQuery = db.Projects.Where(p => !p.IsArchived);
        var userId = _auth.UserId;
        bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
        bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
        bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

        if (userId.HasValue)
        {
            if (isManager)
                projectQuery = projectQuery.Where(p => p.ManagerId == userId.Value);
            else if (isForeman)
            {
                var foremanProjectIds = await db.ProjectMembers
                    .Where(m => m.UserId == userId.Value)
                    .Select(m => m.ProjectId)
                    .ToListAsync();
                projectQuery = projectQuery.Where(p => foremanProjectIds.Contains(p.Id));
            }
            else if (isWorker)
            {
                var workerProjectIds = await GetWorkerVisibleProjectIdsAsync(db, userId.Value);
                projectQuery = projectQuery.Where(p => workerProjectIds.Contains(p.Id));
            }
        }

        var projectList = await projectQuery.ToListAsync();
        projectList = projectList
            .OrderBy(p => p.IsMarkedForDeletion ? 1 : 0)
            .ThenBy(p => p.Status switch
            {
                ProjectStatus.Planning   => 0,
                ProjectStatus.InProgress => 1,
                ProjectStatus.Completed  => 2,
                ProjectStatus.Cancelled  => 3,
                _                        => 9
            })
            .ThenBy(p => p.EndDate ?? DateOnly.MaxValue)
            .ThenByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Name)
            .ToList();
        Projects = new ObservableCollection<LocalProject>(projectList);

        var query = db.Tasks.Where(t => !t.IsArchived);

        if (userId.HasValue)
        {
            if (isManager)
                query = query.Where(t => db.Projects.Any(p => p.Id == t.ProjectId && p.ManagerId == userId.Value));
            else if (isForeman)
            {
                var foremanProjectIds = await db.ProjectMembers
                    .Where(m => m.UserId == userId.Value)
                    .Select(m => m.ProjectId)
                    .ToListAsync();
                query = query.Where(t => foremanProjectIds.Contains(t.ProjectId));
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
                var allWorkerTaskIds = workerTaskIds.Concat(workerTaskIdsFromAssignees).Distinct().ToList();
                query = query.Where(t => allWorkerTaskIds.Contains(t.Id));
            }
        }

        if (ProjectFilter.HasValue)
            query = query.Where(t => t.ProjectId == ProjectFilter.Value);

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

        var projectMarkedById = projectList.ToDictionary(p => p.Id, p => p.IsMarkedForDeletion);
        foreach (var t in list)
            t.ProjectIsMarkedForDeletion = projectMarkedById.GetValueOrDefault(t.ProjectId);

        var currentProjectFilter = ProjectFilter;
        var filterOpts = new List<ProjectFilterOption> { new(null, "Все проекты") };
        filterOpts.AddRange(projectList.Select(p => new ProjectFilterOption(p.Id, p.Name)));
        _suppressProjectFilterReload = true;
        try
        {
            ProjectFilterOptions = new ObservableCollection<ProjectFilterOption>(filterOpts);
            if (currentProjectFilter.HasValue && filterOpts.Any(o => o.Id == currentProjectFilter.Value))
                ProjectFilter = currentProjectFilter;
            else if (currentProjectFilter.HasValue)
                ProjectFilter = null;
        }
        finally
        {
            _suppressProjectFilterReload = false;
        }

        // Load stages and recalculate task status/progress from active stages
        var taskIds = list.Select(t => t.Id).ToList();
        var stages = await db.TaskStages
            .Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        foreach (var t in list)
        {
            var taskStages = stages.Where(s => s.TaskId == t.Id).ToList();
            ProgressCalculator.ApplyTaskMetrics(t, taskStages);
        }

        if (StatusFilter == "Пометка удалить")
        {
            list = list.Where(t => t.EffectiveTaskMarkedForDeletion).ToList();
        }
        else if (StatusFilter != "Все")
        {
            var status = StatusFilter switch
            {
                "Запланирована" => TaskStatus.Planned,
                "Выполняется"   => TaskStatus.InProgress,
                "Завершена"     => TaskStatus.Completed,
                _               => (TaskStatus?)null
            };
            if (status.HasValue)
                list = list.Where(t => t.Status == status.Value && !t.EffectiveTaskMarkedForDeletion).ToList();
        }

        // Populate AssignedUserAvatarData for tasks from Users
        var taskAssigneeIds = list.Where(t => t.AssignedUserId.HasValue).Select(t => t.AssignedUserId!.Value).Distinct().ToList();
        if (taskAssigneeIds.Count > 0)
        {
            var taskUserAvatars = await db.Users.Where(u => taskAssigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToListAsync();
            var avDict = taskUserAvatars.ToDictionary(u => u.Id);
            foreach (var t in list)
            {
                if (t.AssignedUserId.HasValue && avDict.TryGetValue(t.AssignedUserId.Value, out var av))
                {
                    t.AssignedUserAvatarData = av.AvatarData;
                    t.AssignedUserAvatarPath = av.AvatarPath;
                }
            }
        }

        var searchTerm = SearchHelper.Normalize(SearchText);
        if (searchTerm is not null)
            list = list.Where(t => SearchHelper.ContainsIgnoreCase(t.Name, searchTerm) ||
                SearchHelper.ContainsIgnoreCase(t.Description, searchTerm)).ToList();

        list = list
            .OrderBy(t => t.EffectiveTaskMarkedForDeletion)
            .ThenBy(t => t.Status switch
            {
                TaskStatus.Planned    => 0,
                TaskStatus.InProgress => 1,
                TaskStatus.Paused     => 2,
                TaskStatus.Completed  => 3,
                _                     => 4
            })
            .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(t => t.UpdatedAt)
            .ThenBy(t => t.Name)
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
                g.OrderBy(t => t.EffectiveTaskMarkedForDeletion).ThenBy(t => StatusOrder(t.Status))
                    .ThenBy(t => t.DueDate ?? DateOnly.MaxValue).ThenByDescending(t => t.UpdatedAt).ThenBy(t => t.Name).ToList()))
            .ToList();
        TaskGroups = new ObservableCollection<ProjectTaskGroup>(groups);
    }

    public void SetProjectFilter(LocalProject? project)
    {
        ProjectFilter = project?.Id;
        ProjectFilterName = project?.Name ?? "Все проекты";
    }

    private static async Task<List<Guid>> GetWorkerVisibleProjectIdsAsync(LocalDbContext db, Guid workerId)
    {
        var fromTaskAssignee = await db.Tasks
            .Where(t => t.AssignedUserId == workerId)
            .Select(t => t.ProjectId)
            .ToListAsync();
        var fromTaskAssignees = await db.TaskAssignees
            .Where(ta => ta.UserId == workerId)
            .Join(db.Tasks, ta => ta.TaskId, t => t.Id, (_, t) => t.ProjectId)
            .ToListAsync();
        var stageTaskIds = await db.StageAssignees
            .Where(sa => sa.UserId == workerId)
            .Join(db.TaskStages, sa => sa.StageId, s => s.Id, (_, s) => s.TaskId)
            .ToListAsync();
        var fromStageAssignees = stageTaskIds.Count > 0
            ? await db.Tasks.Where(t => stageTaskIds.Contains(t.Id)).Select(t => t.ProjectId).ToListAsync()
            : new List<Guid>();
        var fromStageAssigned = await db.TaskStages
            .Where(s => s.AssignedUserId == workerId)
            .Join(db.Tasks, s => s.TaskId, t => t.Id, (_, t) => t.ProjectId)
            .ToListAsync();
        return fromTaskAssignee
            .Concat(fromTaskAssignees)
            .Concat(fromStageAssignees)
            .Concat(fromStageAssigned)
            .Distinct()
            .ToList();
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
            t.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
            var taskStages = stages.Where(s => s.TaskId == t.Id).ToList();
            foreach (var s in taskStages)
            {
                s.TaskIsMarkedForDeletion = t.IsMarkedForDeletion;
                s.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
            }
            ProgressCalculator.ApplyTaskMetrics(t, taskStages);
        }

        ProgressCalculator.ApplyProjectMetrics(project, tasks, stages);

        var managerAv = await db.Users.Where(u => u.Id == project.ManagerId)
            .Select(u => new { u.AvatarData, u.AvatarPath })
            .FirstOrDefaultAsync();
        if (managerAv is not null)
        {
            project.ManagerAvatarData = managerAv.AvatarData;
            project.ManagerAvatarPath = managerAv.AvatarPath;
        }
        return project;
    }

    public async Task SaveNewTaskAsync(CreateTaskRequest req, Guid localId)
    {
        if (!DueDatePolicy.IsAllowed(req.DueDate))
            throw new ArgumentException(DueDatePolicy.PastNotAllowedMessage);

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
        await LogActivityAsync(db, $"Создана задача «{req.Name}»", "Task", localId, ActivityActionKind.Created);
        await LoadAsync();
    }

    private static async Task RecalcProjectStatusAsync(LocalDbContext db, Guid projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && !t.IsMarkedForDeletion && !t.IsArchived).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = taskIds.Count == 0
            ? new List<LocalTaskStage>()
            : await db.TaskStages.Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived).ToListAsync();

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

    public async Task SaveUpdatedTaskAsync(Guid id, UpdateTaskRequest req)
    {
        if (!DueDatePolicy.IsAllowed(req.DueDate))
            throw new ArgumentException(DueDatePolicy.PastNotAllowedMessage);

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
            s.LastModifiedLocally = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        await LogActivityAsync(db, $"Удалена задача «{task.Name}»", "Task", task.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkTaskForDeletionAsync(LocalTask task)
    {
        if (!task.CanToggleTaskDeletionMark) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Tasks.FindAsync(task.Id);
        if (entity is null) return;

        var proj = await db.Projects.FindAsync(entity.ProjectId);
        if (proj?.IsMarkedForDeletion == true) return;

        var wasMarked = entity.IsMarkedForDeletion;
        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;

        var stages = await db.TaskStages.Where(s => s.TaskId == task.Id).ToListAsync();
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
