using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredTasks = [];

    // ─── Task filters ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _taskSearchText = string.Empty;
    [ObservableProperty] private string _taskStatusFilter = "Все";
    [ObservableProperty] private string _taskPriorityFilter = "Все";

    public IReadOnlyList<string> TaskStatusOptions { get; } =
        ["Все", "Запланирована", "Выполняется", "Завершена", "Пометка удалить"];

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
    [ObservableProperty] private string _stageViewMode = "List";
    [ObservableProperty] private ObservableCollection<LocalFile> _files = [];
    [ObservableProperty] private ObservableCollection<LocalProjectMember> _members = [];

    [ObservableProperty] private List<LocalProjectMember> _foremanMembers = [];
    [ObservableProperty] private List<LocalProjectMember> _workerMembers = [];
    [ObservableProperty] private int _totalTasks;
    [ObservableProperty] private int _completedTasksCount;
    [ObservableProperty] private int _inProgressTasksCount;
    [ObservableProperty] private int _overdueTasksCount;
    [ObservableProperty] private int _totalStagesCount;
    [ObservableProperty] private int _completedStagesCount;
    [ObservableProperty] private int _inProgressStagesCount;
    [ObservableProperty] private int _overdueStagesCount;
    [ObservableProperty] private int _projectProgressPercent;
    [ObservableProperty] private IList<DonutSegment> _taskStatsSegments = [];
    [ObservableProperty] private IList<DonutSegment> _stageStatsSegments = [];
    [ObservableProperty] private ObservableCollection<LocalMessage> _messages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredPlannedStages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredInProgressStages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredCompletedStages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredMarkedStages = [];

    public ProjectDetailViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    private bool CanMarkStageDeletion() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager" or "Foreman";

    private bool CanDeleteStage() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    // ─── Filter change handlers ────────────────────────────────────────────────
    partial void OnTaskSearchTextChanged(string value) => ApplyTaskFilter();
    partial void OnTaskStatusFilterChanged(string value) => ApplyTaskFilter();
    partial void OnTaskPriorityFilterChanged(string value) => ApplyTaskFilter();

    partial void OnStageSearchTextChanged(string value) => ApplyStageFilter();
    partial void OnStageStatusFilterChanged(string value) => ApplyStageFilter();
    partial void OnStageTaskFilterChanged(Guid? value) => ApplyStageFilter();

    private void ClearProjectData()
    {
        Tasks = [];
        FilteredTasks = [];
        FilteredPlannedStages = [];
        FilteredInProgressStages = [];
        FilteredCompletedStages = [];
        FilteredMarkedStages = [];
        AllStages = [];
        FilteredStages = [];
        Files = [];
        Members = [];
        ForemanMembers = [];
        WorkerMembers = [];
        Messages = [];
        TotalTasks = 0;
        CompletedTasksCount = 0;
        InProgressTasksCount = 0;
        OverdueTasksCount = 0;
        TotalStagesCount = 0;
        CompletedStagesCount = 0;
        InProgressStagesCount = 0;
        OverdueStagesCount = 0;
        ProjectProgressPercent = 0;
        TaskStatsSegments = [];
        StageStatsSegments = [];
        _goBackAction?.Invoke();
    }

    public void SetProject(LocalProject project, Action? goBackAction = null)
    {
        Project = project;
        _goBackAction = goBackAction;
    }

    public async Task LoadAsync()
    {
        if (Project is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Reload project from DB to get latest IsMarkedForDeletion and Status.
        // Важно: не присваивать Project до заполнения ManagerAvatar* — иначе UI успевает отрисовать
        // пустой аватар, а без INPC на NotMapped-свойствах Image не обновится (как у участников:
        // у них AvatarData выставляется до попадания в коллекцию).
        var projectEntity = await db.Projects.FindAsync(Project.Id);
        if (projectEntity is not null)
        {
            var managerAv = await db.Users
                .Where(u => u.Id == projectEntity.ManagerId)
                .Select(u => new { u.AvatarData, u.AvatarPath })
                .FirstOrDefaultAsync();
            byte[]? mgrAvatarData = null;
            string? mgrAvatarPath = null;
            if (managerAv is not null)
            {
                mgrAvatarData = managerAv.AvatarData;
                mgrAvatarPath = managerAv.AvatarPath;
                if ((mgrAvatarData is null || mgrAvatarData.Length == 0)
                    && !string.IsNullOrWhiteSpace(mgrAvatarPath))
                {
                    var fromFile = AvatarHelper.FileToBytes(mgrAvatarPath);
                    if (fromFile is { Length: > 0 })
                        mgrAvatarData = fromFile;
                }
            }

            projectEntity.ManagerAvatarData = mgrAvatarData;
            projectEntity.ManagerAvatarPath = mgrAvatarPath;
            Project = projectEntity;
        }

        var userId = _auth.UserId;
        bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
        bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
        bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

        if (userId.HasValue)
        {
            if (isManager && Project.ManagerId != userId.Value)
            {
                ClearProjectData();
                return;
            }
            if (isForeman)
            {
                var isMember = await db.ProjectMembers
                    .AnyAsync(m => m.ProjectId == Project.Id && m.UserId == userId.Value);
                if (!isMember)
                {
                    ClearProjectData();
                    return;
                }
            }
            if (isWorker)
            {
                var hasAssignedTask = await db.Tasks
                    .AnyAsync(t => t.ProjectId == Project.Id && (t.AssignedUserId == userId.Value ||
                        db.TaskAssignees.Any(ta => ta.TaskId == t.Id && ta.UserId == userId.Value)));
                var hasAssignedStage = await db.TaskStages
                    .Where(s => db.Tasks.Any(t => t.Id == s.TaskId && t.ProjectId == Project.Id))
                    .AnyAsync(s => s.AssignedUserId == userId.Value ||
                        db.StageAssignees.Any(sa => sa.StageId == s.Id && sa.UserId == userId.Value));
                if (!hasAssignedTask && !hasAssignedStage)
                {
                    ClearProjectData();
                    return;
                }
            }
        }

        var tasksQuery = db.Tasks.Where(t => t.ProjectId == Project.Id && !t.IsArchived);

        if (userId.HasValue && isWorker)
        {
            var workerTaskIds = await db.Tasks
                .Where(t => t.ProjectId == Project.Id && t.AssignedUserId == userId.Value)
                .Select(t => t.Id)
                .ToListAsync();
            var workerTaskIdsFromAssignees = await db.TaskAssignees
                .Where(ta => ta.UserId == userId.Value)
                .Join(db.Tasks.Where(t => t.ProjectId == Project.Id), ta => ta.TaskId, t => t.Id, (_, t) => t.Id)
                .ToListAsync();
            var allWorkerTaskIds = workerTaskIds.Concat(workerTaskIdsFromAssignees).Distinct().ToList();
            tasksQuery = tasksQuery.Where(t => allWorkerTaskIds.Contains(t.Id));
        }

        var tasks = await tasksQuery
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // Load stages and compute TotalStages/CompletedStages + auto task status for each task
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stagesQuery = db.TaskStages
            .Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived);
        if (userId.HasValue && isWorker)
        {
            var workerStageIds = await db.StageAssignees
                .Where(sa => sa.UserId == userId.Value)
                .Select(sa => sa.StageId)
                .ToListAsync();
            var workerStageIdsFromAssigned = await db.TaskStages
                .Where(s => taskIds.Contains(s.TaskId) && s.AssignedUserId == userId.Value)
                .Select(s => s.Id)
                .ToListAsync();
            var allWorkerStageIds = workerStageIds.Concat(workerStageIdsFromAssigned).Distinct().ToList();
            stagesQuery = stagesQuery.Where(s => allWorkerStageIds.Contains(s.Id));
        }
        var stages = await stagesQuery.OrderBy(s => s.CreatedAt).ToListAsync();

        var projectMarked = Project?.IsMarkedForDeletion ?? false;
        foreach (var task in tasks)
        {
            task.ProjectIsMarkedForDeletion = projectMarked;
            var taskStages = stages.Where(s => s.TaskId == task.Id).ToList();
            foreach (var s in taskStages)
            {
                s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
                s.ProjectIsMarkedForDeletion = projectMarked;
            }
            ProgressCalculator.ApplyTaskMetrics(task, taskStages);
        }

        // Persist task status changes and recalc project status
        await RecalcAndSaveTaskStatusesAsync(db, tasks);
        await RecalcProjectStatusAsync(db);

        // Populate AssignedUserAvatarData for tasks from Users
        var taskAssigneeIds = tasks.Where(t => t.AssignedUserId.HasValue).Select(t => t.AssignedUserId!.Value).Distinct().ToList();
        if (taskAssigneeIds.Count > 0)
        {
            var taskUserAvatars = await db.Users.Where(u => taskAssigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToListAsync();
            var avDict = taskUserAvatars.ToDictionary(u => u.Id);
            foreach (var t in tasks)
            {
                if (t.AssignedUserId.HasValue && avDict.TryGetValue(t.AssignedUserId.Value, out var av))
                {
                    t.AssignedUserAvatarData = av.AvatarData;
                    t.AssignedUserAvatarPath = av.AvatarPath;
                }
            }
        }

        Tasks = new ObservableCollection<LocalTask>(tasks);
        ApplyTaskFilter();

        if (Project is not null)
            ProgressCalculator.ApplyProjectMetrics(Project, tasks, stages);

        TotalTasks = Project?.TotalTasks ?? 0;
        CompletedTasksCount = Project?.CompletedTasks ?? 0;
        InProgressTasksCount = Project?.InProgressTasks ?? 0;
        OverdueTasksCount = Project?.OverdueTasks ?? 0;
        ProjectProgressPercent = Project?.ProgressPercent ?? 0;

        int plannedCount = tasks.Count(t => !t.EffectiveTaskMarkedForDeletion && !t.IsArchived && t.Status == TaskStatus.Planned);
        TaskStatsSegments = new List<DonutSegment>
        {
            new() { Label = "Завершено",    Value = CompletedTasksCount,  Color = Color.FromRgb(0x22, 0xC5, 0x5E) },
            new() { Label = "В процессе",   Value = InProgressTasksCount, Color = Color.FromRgb(0xEA, 0xB3, 0x08) },
            new() { Label = "Просрочено",   Value = OverdueTasksCount,    Color = Color.FromRgb(0xEF, 0x44, 0x44) },
            new() { Label = "Запланировано",Value = plannedCount,          Color = Color.FromRgb(0x3B, 0x82, 0xF6) },
        };

        var activeStages = stages.Where(s => !s.EffectiveMarkedForDeletion).ToList();
        TotalStagesCount = activeStages.Count;
        CompletedStagesCount = activeStages.Count(s => s.Status == StageStatus.Completed);
        InProgressStagesCount = activeStages.Count(s => s.Status == StageStatus.InProgress);
        OverdueStagesCount = activeStages.Count(s => s.IsOverdue);
        int plannedStagesCount = activeStages.Count(s => s.Status == StageStatus.Planned && !s.IsOverdue);

        StageStatsSegments = new List<DonutSegment>
        {
            new() { Label = "Завершено",    Value = CompletedStagesCount,  Color = Color.FromRgb(0x22, 0xC5, 0x5E) },
            new() { Label = "В процессе",   Value = InProgressStagesCount, Color = Color.FromRgb(0xEA, 0xB3, 0x08) },
            new() { Label = "Просрочено",   Value = OverdueStagesCount,    Color = Color.FromRgb(0xEF, 0x44, 0x44) },
            new() { Label = "Запланировано",Value = plannedStagesCount,     Color = Color.FromRgb(0x3B, 0x82, 0xF6) },
        };

        // Populate TaskName and AssignedUserAvatarData for each stage
        var taskNameDict = tasks.ToDictionary(t => t.Id, t => t.Name);
        var stageAssigneeIds = stages.Where(s => s.AssignedUserId.HasValue).Select(s => s.AssignedUserId!.Value).Distinct().ToList();
        if (stageAssigneeIds.Count > 0)
        {
            var stageUserAvatars = await db.Users.Where(u => stageAssigneeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToListAsync();
            var stageAvDict = stageUserAvatars.ToDictionary(u => u.Id);
            foreach (var s in stages)
            {
                if (s.AssignedUserId.HasValue && stageAvDict.TryGetValue(s.AssignedUserId.Value, out var av))
                {
                    s.AssignedUserAvatarData = av.AvatarData;
                    s.AssignedUserAvatarPath = av.AvatarPath;
                }
            }
        }
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
        var projectId = Project!.Id;
        var files = await db.Files
            .Where(f => f.ProjectId == projectId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
        Files = new ObservableCollection<LocalFile>(files);

        // Load project members (executors) with AvatarData/AvatarPath from Users
        var members = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.UserName)
            .ToListAsync();
        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarPath, u.AvatarData, u.SubRole, u.AdditionalSubRoles })
            .ToDictionaryAsync(u => u.Id);
        foreach (var m in members)
        {
            if (userAvatars.TryGetValue(m.UserId, out var av))
            {
                m.AvatarPath = av.AvatarPath;
                m.SubRole               = av.SubRole;
                m.AdditionalSubRolesJson = av.AdditionalSubRoles;
                var data = av.AvatarData;
                if ((data is null || data.Length == 0) && !string.IsNullOrWhiteSpace(av.AvatarPath))
                {
                    var fromFile = AvatarHelper.FileToBytes(av.AvatarPath);
                    if (fromFile is { Length: > 0 })
                        data = fromFile;
                }
                m.AvatarData = data;
            }
        }

        foreach (var m in members)
            m.IsUserPeekInteractive = UserPeekAccess.CanInteractPeekRow(_auth, db, m.UserRole);

        Members = new ObservableCollection<LocalProjectMember>(members);
        ForemanMembers = [.. members.Where(m => m.UserRole is "Foreman" or "Прораб")];
        WorkerMembers  = [.. members.Where(m => m.UserRole is "Worker" or "Работник")];

        // Load project messages (discussion) with AvatarData from Users
        var messages = await db.Messages
            .Where(m => m.ProjectId == projectId)
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

    private void ApplyTaskFilter()
    {
        var query = Tasks.AsEnumerable();

        if (SearchHelper.Normalize(TaskSearchText) is { } taskTerm)
            query = query.Where(t =>
                SearchHelper.ContainsIgnoreCase(t.Name, taskTerm) ||
                SearchHelper.ContainsIgnoreCase(t.Description, taskTerm));

        if (TaskStatusFilter == "Пометка удалить")
        {
            query = query.Where(t => t.EffectiveTaskMarkedForDeletion);
        }
        else if (TaskStatusFilter != "Все")
        {
            var status = TaskStatusFilter switch
            {
                "Запланирована" => TaskStatus.Planned,
                "Выполняется"   => TaskStatus.InProgress,
                "Завершена"     => TaskStatus.Completed,
                _               => (TaskStatus?)null
            };
            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value && !t.EffectiveTaskMarkedForDeletion);
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

        // Статус (план → в работе → пауза → завершена), пометка удаления в конце; затем дата срока и обновление
        var list = query
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

        FilteredTasks = new ObservableCollection<LocalTask>(list);
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
            query = query.Where(s => s.EffectiveMarkedForDeletion);
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
                query = query.Where(s => s.Status == targetStatus.Value && !s.EffectiveMarkedForDeletion);
        }

        var list = query
            .OrderBy(s => s.EffectiveMarkedForDeletion)
            .ThenBy(s => s.Status switch
            {
                StageStatus.Planned    => 0,
                StageStatus.InProgress => 1,
                StageStatus.Completed  => 2,
                _                      => 9
            })
            .ThenBy(s => s.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.TaskName)
            .ThenBy(s => s.Name)
            .ToList();
        FilteredStages = new ObservableCollection<LocalTaskStage>(list);

        StageItem MakeStageItem(LocalTaskStage s) => new()
        {
            Stage = s,
            TaskId = s.TaskId,
            TaskName = s.TaskName,
            ProjectId = Project?.Id ?? Guid.Empty,
            ProjectName = Project?.Name ?? "—"
        };

        var markedItems = list.Where(s => s.EffectiveMarkedForDeletion).Select(MakeStageItem).ToList();
        var stageItems = list.Where(s => !s.EffectiveMarkedForDeletion).Select(MakeStageItem).ToList();

        FilteredMarkedStages = new ObservableCollection<StageItem>(markedItems);
        FilteredPlannedStages = new ObservableCollection<StageItem>(stageItems.Where(s => s.Stage.Status == StageStatus.Planned));
        FilteredInProgressStages = new ObservableCollection<StageItem>(stageItems.Where(s => s.Stage.Status == StageStatus.InProgress));
        FilteredCompletedStages = new ObservableCollection<StageItem>(stageItems.Where(s => s.Stage.Status == StageStatus.Completed));
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
        project.IsMarkedForDeletion = req.IsMarkedForDeletion;
        project.IsArchived = req.IsArchived;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await RecalcProjectStatusAsync(db);
        await _sync.QueueOperationAsync("Project", id, SyncOperation.Update, req);

        await LoadAsync();
    }

    [RelayCommand]
    private void GoBack() => _goBackAction?.Invoke();

    [RelayCommand]
    private void SwitchTab(string tab) => ActiveTab = tab;

    [RelayCommand]
    private void SwitchStageView(string mode) => StageViewMode = mode;

    [RelayCommand]
    private async Task MarkStageForDeletionAsync(LocalTaskStage stage)
    {
        if (!CanMarkStageDeletion()) return;
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
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Update, SyncPayloads.Stage(entity));
        var action = entity.IsMarkedForDeletion ? "Помечен для удаления" : "Снята пометка удаления";
        var actionType = entity.IsMarkedForDeletion ? ActivityActionKind.MarkedForDeletion : ActivityActionKind.UnmarkedForDeletion;
        await LogActivityAsync(db, $"{action}: этап «{stage.Name}»", "Stage", stage.Id, actionType);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteStageAsync(LocalTaskStage stage)
    {
        if (!CanDeleteStage()) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;
        entity.IsArchived = true;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LastModifiedLocally = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Stage", entity.Id, SyncOperation.Update, SyncPayloads.Stage(entity));
        await LogActivityAsync(db, $"Этап «{stage.Name}» перемещён в архив", "Stage", stage.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ChangeStageStatusAsync((LocalTaskStage stage, StageStatus newStatus) args)
    {
        var (stage, newStatus) = args;
        if (stage.EffectiveMarkedForDeletion) return;
        var req = new UpdateStageRequest(stage.Name, stage.Description, stage.AssignedUserId, newStatus, stage.DueDate, stage.IsMarkedForDeletion, stage.IsArchived);
        var taskVm = App.Services.GetRequiredService<TaskDetailViewModel>();
        var task = Tasks.FirstOrDefault(t => t.Id == stage.TaskId);
        if (task is null) return;
        taskVm.SetTask(task);
        await taskVm.SaveUpdatedStageAsync(stage.Id, req);
        await using var db = await _dbFactory.CreateDbContextAsync();
        await RecalcProjectStatusAsync(db);
        await LoadAsync();
    }

    public async Task SaveNewTaskAsync(CreateTaskRequest req, Guid localId)
    {
        if (!DueDatePolicy.IsAllowed(req.DueDate))
            throw new ArgumentException(DueDatePolicy.PastNotAllowedMessage);

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
        var syncTaskReq = req with { IsMarkedForDeletion = task.IsMarkedForDeletion, IsArchived = task.IsArchived };
        await _sync.QueueOperationAsync("Task", id, SyncOperation.Update, syncTaskReq);
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

        // Cascade archive to all stages of this task
        var stages = await db.TaskStages.Where(s => s.TaskId == task.Id).ToListAsync();
        foreach (var s in stages)
        {
            s.IsArchived = true;
            s.IsSynced = false;
            s.UpdatedAt = DateTime.UtcNow;
            s.LastModifiedLocally = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await RecalcProjectStatusAsync(db);
        await _sync.QueueOperationAsync("Task", entity.Id, SyncOperation.Update, SyncPayloads.Task(entity));
        foreach (var s in stages)
            await _sync.QueueOperationAsync("Stage", s.Id, SyncOperation.Update, SyncPayloads.Stage(s));
        await LogActivityAsync(db, $"Задача «{task.Name}» перемещена в архив", "Task", task.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkTaskForDeletionAsync(LocalTask task)
    {
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
        await _sync.QueueOperationAsync("Task", entity.Id, SyncOperation.Update, SyncPayloads.Task(entity));

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

        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).ToListAsync();

        foreach (var t in tasks)
        {
            t.IsMarkedForDeletion = project.IsMarkedForDeletion;
            t.IsSynced = false;
            t.UpdatedAt = DateTime.UtcNow;
        }

        if (!project.IsMarkedForDeletion)
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
        await _sync.QueueOperationAsync("Project", project.Id, SyncOperation.Update, SyncPayloads.Project(project));
        foreach (var t in tasks)
            await _sync.QueueOperationAsync("Task", t.Id, SyncOperation.Update, SyncPayloads.Task(t));

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
            Id = Guid.NewGuid(),
            ProjectId = Project.Id,
            UserId = _auth.UserId ?? Guid.Empty,
            UserName = userName,
            UserInitials = initials,
            UserColor = "#1B6EC2",
            UserRole = RoleToRussian(_auth.UserRole),
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        if (_auth.UserId is { } uid)
        {
            var avatar = await db.Users
                .Where(u => u.Id == uid)
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

    /// <summary>Persists task status/stages and recalculates project status.</summary>
    private async Task RecalcAndSaveTaskStatusesAsync(LocalDbContext db, List<LocalTask> tasks)
    {
        foreach (var t in tasks)
        {
            var entity = await db.Tasks.FindAsync(t.Id);
            if (entity is null) continue;
            entity.TotalStages = t.TotalStages;
            entity.CompletedStages = t.CompletedStages;
            entity.InProgressStages = t.InProgressStages;
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

        var tasks = await db.Tasks.Where(t => t.ProjectId == project.Id && !t.IsMarkedForDeletion && !t.IsArchived).ToListAsync();
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

        if (Project is not null)
            Project.Status = project.Status;
    }

    public static string RoleToRussian(string? role) => role switch
    {
        "Administrator" or "Admin" => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager" => "Менеджер",
        "Foreman" or "Прораб" => "Прораб",
        "Worker" or "Работник" => "Работник",
        _ => role ?? "—"
    };
}
