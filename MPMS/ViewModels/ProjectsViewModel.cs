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

public partial class ProjectsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private CancellationTokenSource _loadCts = new();

    [ObservableProperty] private ObservableCollection<LocalProject> _projects = [];
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _recentActivities = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "Все";

    public IReadOnlyList<string> StatusOptions { get; } =
        ["Все", "Планирование", "В работе", "Завершён", "Отменён", "Пометка удалить"];

    public ProjectsViewModel(IDbContextFactory<LocalDbContext> dbFactory,
        ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        // Cancel any previous in-flight load
        _loadCts.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var query = db.Projects.Where(p => !p.IsArchived);

            var userId = _auth.UserId;
            bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
            bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
            bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

            if (userId.HasValue)
            {
                if (isManager)
                {
                    // Менеджер видит только проекты, где он ответственный
                    query = query.Where(p => p.ManagerId == userId.Value);
                }
                else if (isForeman)
                {
                    // Прораб видит только проекты, где он назначен
                    var assignedProjectIds = await db.ProjectMembers
                        .Where(m => m.UserId == userId.Value)
                        .Select(m => m.ProjectId)
                        .ToListAsync(ct);
                    query = query.Where(p => assignedProjectIds.Contains(p.Id));
                }
                else if (isWorker)
                {
                    // Рабочий видит только проекты, где у него есть назначенные задачи или этапы
                    var projectIdsFromTaskAssignee = await db.Tasks
                        .Where(t => t.AssignedUserId == userId.Value)
                        .Select(t => t.ProjectId)
                        .ToListAsync(ct);
                    var projectIdsFromTaskAssignees = await db.TaskAssignees
                        .Where(ta => ta.UserId == userId.Value)
                        .Join(db.Tasks, ta => ta.TaskId, t => t.Id, (_, t) => t.ProjectId)
                        .ToListAsync(ct);
                    var projectIdsFromStageAssignees = await db.StageAssignees
                        .Where(sa => sa.UserId == userId.Value)
                        .Join(db.TaskStages, sa => sa.StageId, s => s.Id, (_, s) => s.TaskId)
                        .Join(db.Tasks, tid => tid, t => t.Id, (_, t) => t.ProjectId)
                        .ToListAsync(ct);
                    var projectIdsFromStageAssigned = await db.TaskStages
                        .Where(s => s.AssignedUserId == userId.Value)
                        .Join(db.Tasks, s => s.TaskId, t => t.Id, (_, t) => t.ProjectId)
                        .ToListAsync(ct);
                    var workerProjectIds = projectIdsFromTaskAssignee
                        .Concat(projectIdsFromTaskAssignees)
                        .Concat(projectIdsFromStageAssignees)
                        .Concat(projectIdsFromStageAssigned)
                        .Distinct()
                        .ToList();
                    query = query.Where(p => workerProjectIds.Contains(p.Id));
                }
            }

            await LoadInternalAsync(db, query, ct);
        }
        catch (OperationCanceledException) { /* newer call superseded this one */ }
    }

    private async Task LoadInternalAsync(LocalDbContext db, IQueryable<LocalProject> query, CancellationToken ct)
    {
        var searchTerm = SearchHelper.Normalize(SearchText);
        var statusSnapshot = StatusFilter;

        if (statusSnapshot == "Пометка удалить")
        {
            query = query.Where(p => p.IsMarkedForDeletion);
        }

        if (statusSnapshot != "Пометка удалить" && statusSnapshot != "Все")
        {
            var status = statusSnapshot switch
            {
                "Планирование" => ProjectStatus.Planning,
                "В работе"     => ProjectStatus.InProgress,
                "Завершён"     => ProjectStatus.Completed,
                "Отменён"      => ProjectStatus.Cancelled,
                _              => (ProjectStatus?)null
            };
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value && !p.IsMarkedForDeletion);
        }

        var list = (await query.ToListAsync(ct)).ToList();
        ct.ThrowIfCancellationRequested();

        if (searchTerm is not null)
            list = list.Where(p => SearchHelper.ContainsIgnoreCase(p.Name, searchTerm) ||
                SearchHelper.ContainsIgnoreCase(p.Client, searchTerm)).ToList();

        // Populate progress stats for each project (TotalTasks, CompletedTasks, InProgressTasks — как в ProjectDetailViewModel)
        var allTasks = await db.Tasks.ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        var allStages = await db.TaskStages.ToListAsync(ct);
        ct.ThrowIfCancellationRequested();
        foreach (var project in list)
        {
            var projTasks = allTasks.Where(t => t.ProjectId == project.Id && !t.IsMarkedForDeletion && !t.IsArchived).ToList();
            var taskIds = projTasks.Select(t => t.Id).ToList();
            var projStages = allStages.Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived).ToList();
            foreach (var t in projTasks)
            {
                t.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
                var stages = projStages.Where(s => s.TaskId == t.Id).ToList();
                foreach (var s in stages)
                {
                    s.TaskIsMarkedForDeletion = t.IsMarkedForDeletion;
                    s.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
                }
                ProgressCalculator.ApplyTaskMetrics(t, stages);
            }
            ProgressCalculator.ApplyProjectMetrics(project, projTasks, projStages);
        }

        // Sort: non-deleted first by status, then by progress desc, then by date
        list = list
            .OrderBy(p => p.IsMarkedForDeletion)
            .ThenBy(p => p.Status switch
            {
                ProjectStatus.Planning    => 0,
                ProjectStatus.InProgress  => 1,
                ProjectStatus.Completed   => 2,
                ProjectStatus.Cancelled   => 3,
                _                         => 4
            })
            .ThenBy(p => p.ProgressPercent)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();

        // Populate ManagerAvatarData/ManagerAvatarPath from Users
        var managerIds = list.Select(p => p.ManagerId).Distinct().ToList();
        if (managerIds.Count > 0)
        {
            var managerAvatars = await db.Users
                .Where(u => managerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToDictionaryAsync(u => u.Id, ct);
            foreach (var p in list)
            {
                if (managerAvatars.TryGetValue(p.ManagerId, out var av))
                {
                    p.ManagerAvatarData = av.AvatarData;
                    p.ManagerAvatarPath = av.AvatarPath;
                }
            }
        }

        Projects = new ObservableCollection<LocalProject>(list);

        // Load recent activity log with role-based filtering
        var activities = await ActivityFilterService.GetFilteredActivitiesAsync(db, _auth, 10, excludeAuthEvents: true, ct);
        RecentActivities = new ObservableCollection<LocalActivityLog>(activities);
    }

    public async Task SaveNewProjectAsync(CreateProjectRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var managerName = await db.Users
            .Where(u => u.Id == req.ManagerId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync() ?? "—";

        var project = new LocalProject
        {
            Id = localId,
            Name = req.Name,
            Description = req.Description,
            Client = req.Client,
            Address = req.Address,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            Status = ProjectStatus.Planning,
            ManagerId = req.ManagerId,
            ManagerName = managerName,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Project", localId, SyncOperation.Create,
            req with { Id = localId });

        await LogActivityAsync(db, $"Создан проект «{req.Name}»", "Project", localId, ActivityActionKind.Created);
        await LoadAsync();
    }

    public async Task SaveUpdatedProjectAsync(Guid id, UpdateProjectRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(id);
        if (project is null) return;

        var managerName = await db.Users
            .Where(u => u.Id == req.ManagerId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync() ?? project.ManagerName;

        project.Name = req.Name;
        project.Description = req.Description;
        project.Client = req.Client;
        project.Address = req.Address;
        project.StartDate = req.StartDate;
        project.EndDate = req.EndDate;
        // Status is auto-calculated from tasks, do not override from request
        project.ManagerId = req.ManagerId;
        project.ManagerName = managerName;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Project", id, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлён проект «{req.Name}»", "Project", id, ActivityActionKind.Updated);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(LocalProject project)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Projects.FindAsync(project.Id);
        if (entity is null) return;

        // Move to archive — cascade IsArchived to all tasks and their stages
        entity.IsArchived = true;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;

        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).ToListAsync();
        foreach (var t in tasks) { t.IsArchived = true; t.IsSynced = false; t.UpdatedAt = DateTime.UtcNow; }
        foreach (var s in stages)
        {
            s.IsArchived = true;
            s.IsSynced = false;
            s.UpdatedAt = DateTime.UtcNow;
            s.LastModifiedLocally = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await LogActivityAsync(db, $"Проект «{project.Name}» перемещён в архив", "Project", project.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkProjectForDeletionAsync(LocalProject project)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Projects.FindAsync(project.Id);
        if (entity is null) return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;

        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).ToListAsync();

        foreach (var t in tasks)
        {
            t.IsMarkedForDeletion = entity.IsMarkedForDeletion;
            t.IsSynced = false;
            t.UpdatedAt = DateTime.UtcNow;
        }

        if (!entity.IsMarkedForDeletion)
        {
            foreach (var s in stages)
            {
                s.IsMarkedForDeletion = false;
                s.IsSynced = false;
                s.UpdatedAt = DateTime.UtcNow;
                s.LastModifiedLocally = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();

        var action = entity.IsMarkedForDeletion ? "Помечен для удаления" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: проект «{project.Name}»", "Project", project.Id, actionType);
        await LoadAsync();
    }

    private async Task LogActivityAsync(LocalDbContext db, string actionText,
        string entityType, Guid entityId, string? actionType = null)
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
