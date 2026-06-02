using System.Collections.ObjectModel;
using System.Windows.Input;
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
using MPMS.Views.Overlays;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

public partial class ProjectDetailViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly IUserSettingsService _settings;
    private Action? _goBackAction;

    [ObservableProperty] private LocalProject? _project;
    public ICommand? BackCommand { get; private set; }

    // ─── Коллекции задач
    [ObservableProperty] private ObservableCollection<LocalTask> _tasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _filteredTasks = [];

    // ─── Фильтры задач
    [ObservableProperty] private string _taskSearchText = string.Empty;
    [ObservableProperty] private string _taskStatusFilter = "Все";
    [ObservableProperty] private string _taskPriorityFilter = "Все";

    public IReadOnlyList<string> TaskStatusOptions { get; } =
        ["Все", "Запланирована", "Выполняется", "Завершена", "Пометка удаления"];

    public IReadOnlyList<string> TaskPriorityOptions { get; } =
        ["Все", "Низкий", "Средний", "Высокий", "Критический"];

    // ─── Коллекции этапов и фильтры
    [ObservableProperty] private ObservableCollection<LocalTaskStage> _allStages = [];
    [ObservableProperty] private ObservableCollection<LocalTaskStage> _filteredStages = [];
    [ObservableProperty] private ObservableCollection<ProjectStageGroup> _projectStageGroups = [];
    [ObservableProperty] private string _stageSearchText = string.Empty;
    [ObservableProperty] private string _stageStatusFilter = "Все статусы";

    // Фильтр этапов по задаче внутри проекта
    [ObservableProperty] private Guid? _stageTaskFilter;
    [ObservableProperty] private ObservableCollection<TaskFilterOption> _stageTaskFilterOptions = [];

    public List<string> StageStatusOptions { get; } =
        ["Все статусы", "Запланирован", "Выполняется", "Завершён", "Пометка удаления"];

    // ─── Состояние UI и другие сущности
    [ObservableProperty] private string _activeTab = "Tasks";
    [ObservableProperty] private string _stageViewMode = "List";
    public FilesControlViewModel FilesControlVM { get; }
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
    [ObservableProperty] private IList<DonutSegment> _budgetByStageStatusSegments = [];
    [ObservableProperty] private IList<DonutSegment> _workTypeDistributionSegments = [];
    [ObservableProperty] private ObservableCollection<LocalMessage> _messages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredPlannedStages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredInProgressStages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredCompletedStages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredMarkedStages = [];

    [ObservableProperty] private ObservableCollection<ProjectSummaryTaskGroupVm> _projectSummaryTaskGroups = [];
    [ObservableProperty] private ObservableCollection<ProjectSummaryCatalogLineVm> _projectSummaryServiceLines = [];
    [ObservableProperty] private ObservableCollection<ProjectSummaryCatalogLineVm> _projectSummaryMaterialLines = [];
    [ObservableProperty] private decimal _projectSummaryServicesSubtotal;
    [ObservableProperty] private decimal _projectSummaryMaterialsSubtotal;
    [ObservableProperty] private decimal _projectSummaryAdjustedServicesTotal;
    [ObservableProperty] private decimal _projectSummaryAdjustedMaterialsTotal;
    [ObservableProperty] private decimal _projectSummaryGrandTotal;
    [ObservableProperty] private decimal _projectSummarySubtotal;
    [ObservableProperty] private int _projectSummaryStagesWithPricingCount;

    [ObservableProperty] private string _projectSummarySection = "Receipt";
    [ObservableProperty] private Guid? _projectSummaryReceiptTaskFilter;
    [ObservableProperty] private Guid? _projectSummaryReceiptStageFilter;
    [ObservableProperty] private TaskFilterOption? _projectSummaryReceiptSelectedTask;
    [ObservableProperty] private StageFilterOption? _projectSummaryReceiptSelectedStage;
    [ObservableProperty] private ObservableCollection<TaskFilterOption> _projectSummaryReceiptTaskOptions = [];
    [ObservableProperty] private ObservableCollection<StageFilterOption> _projectSummaryReceiptStageOptions = [];
    [ObservableProperty] private ObservableCollection<ProjectSummaryReceiptStageSectionVm> _projectSummaryFilteredServiceSections = [];
    [ObservableProperty] private ObservableCollection<ProjectSummaryReceiptStageSectionVm> _projectSummaryFilteredMaterialSections = [];
    [ObservableProperty] private bool _projectSummaryReceiptServicesGroupByStage = true;
    [ObservableProperty] private bool _projectSummaryReceiptMaterialsGroupByStage = true;
    [ObservableProperty] private int _projectSummaryReceiptFilteredStageCount;
    [ObservableProperty] private decimal _projectSummaryFilteredServicesSubtotal;
    [ObservableProperty] private decimal _projectSummaryFilteredMaterialsSubtotal;
    [ObservableProperty] private decimal _projectSummaryFilteredAdjustedServicesTotal;
    [ObservableProperty] private decimal _projectSummaryFilteredAdjustedMaterialsTotal;
    [ObservableProperty] private decimal _projectSummaryFilteredGrandTotal;

    private List<LocalTask> _summaryTasks = [];
    private List<LocalTaskStage> _summaryStages = [];
    private List<LocalStageWorkType> _summaryWorkTypes = [];
    private List<LocalStageMaterial> _summaryMaterials = [];

    public bool ShowProjectSummaryTab =>
        !string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(_auth.UserRole, "Работник", StringComparison.OrdinalIgnoreCase);

    public decimal ProjectSummaryProgressMaximum =>
        Math.Max(1m, ProjectSummaryAdjustedServicesTotal + ProjectSummaryAdjustedMaterialsTotal);

    public bool ProjectSummaryHasServiceAdjustment =>
        ProjectSummaryServicesSubtotal != ProjectSummaryAdjustedServicesTotal;

    public bool ProjectSummaryHasMaterialAdjustment =>
        ProjectSummaryMaterialsSubtotal != ProjectSummaryAdjustedMaterialsTotal;

    public bool ProjectSummaryHasAdjustment =>
        ProjectSummarySubtotal != ProjectSummaryGrandTotal;

    public bool ProjectSummaryFilteredHasServiceAdjustment =>
        ProjectSummaryFilteredServicesSubtotal != ProjectSummaryFilteredAdjustedServicesTotal;

    public bool ProjectSummaryFilteredHasMaterialAdjustment =>
        ProjectSummaryFilteredMaterialsSubtotal != ProjectSummaryFilteredAdjustedMaterialsTotal;

    public decimal ProjectSummaryFilteredProgressMaximum =>
        Math.Max(1m, ProjectSummaryFilteredAdjustedServicesTotal + ProjectSummaryFilteredAdjustedMaterialsTotal);

    public bool ProjectSummaryReceiptShowGroupToggle => ProjectSummaryReceiptFilteredStageCount > 1;

    public string ProjectSummaryReceiptServicesGroupButtonText =>
        ProjectSummaryReceiptServicesGroupByStage ? "Объединить" : "По этапам";

    public string ProjectSummaryReceiptMaterialsGroupButtonText =>
        ProjectSummaryReceiptMaterialsGroupByStage ? "Объединить" : "По этапам";

    public ProjectDetailViewModel(
        IDbContextFactory<LocalDbContext> dbFactory,
        ISyncService sync,
        IAuthService auth,
        IApiService api,
        IUserSettingsService settings,
        IPageUiStateStore uiState,
        SidebarFooterViewModel sidebarFooter)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
        _settings = settings;
        FilesControlVM = new FilesControlViewModel(dbFactory, auth, api, settings, sync, uiState, sidebarFooter);
        _stageViewMode = _settings.GetValue("StagesViewMode", "List");
    }

    private bool CanMarkStageDeletion() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager" or "Foreman";

    private bool CanDeleteStage() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    private bool CanMarkTaskDeletion() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    // ─── Обработчики изменения фильтров
    partial void OnTaskSearchTextChanged(string value) => ApplyTaskFilter();
    partial void OnTaskStatusFilterChanged(string value) => ApplyTaskFilter();
    partial void OnTaskPriorityFilterChanged(string value) => ApplyTaskFilter();

    partial void OnStageSearchTextChanged(string value) => ApplyStageFilter();
    partial void OnStageStatusFilterChanged(string value) => ApplyStageFilter();
    partial void OnStageTaskFilterChanged(Guid? value) => ApplyStageFilter();

    partial void OnProjectSummaryReceiptSelectedTaskChanged(TaskFilterOption? value)
    {
        ProjectSummaryReceiptTaskFilter = value?.Id;
        UpdateProjectSummaryReceiptStageOptions(resetStageSelection: true);
        ApplyProjectSummaryReceiptFilter();
    }

    partial void OnProjectSummaryReceiptSelectedStageChanged(StageFilterOption? value)
    {
        ProjectSummaryReceiptStageFilter = value?.Id;
        ApplyProjectSummaryReceiptFilter();
    }

    partial void OnProjectSummaryReceiptServicesGroupByStageChanged(bool value) =>
        ApplyProjectSummaryReceiptFilter();

    partial void OnProjectSummaryReceiptMaterialsGroupByStageChanged(bool value) =>
        ApplyProjectSummaryReceiptFilter();

    private bool IsProjectSummaryManagerOrAdmin() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    private bool IsProjectSummaryForeman() =>
        _auth.UserRole is "Foreman" or "Прораб";

    public bool CanEditProjectSummaryReceiptRow(ReceiptRowVm row)
    {
        if (!row.IsEditable || !row.StageId.HasValue) return false;
        var stage = _summaryStages.FirstOrDefault(s => s.Id == row.StageId.Value);
        if (stage is null || stage.EffectiveMarkedForDeletion) return false;
        if (stage.Status == StageStatus.Completed) return IsProjectSummaryManagerOrAdmin();
        return IsProjectSummaryManagerOrAdmin() || IsProjectSummaryForeman();
    }

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
        FilesControlVM.AllFiles.Clear();
        FilesControlVM.DisplayedFiles.Clear();
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
        ProjectSummaryTaskGroups = [];
        ProjectSummaryServiceLines = [];
        ProjectSummaryMaterialLines = [];
        ProjectSummaryServicesSubtotal = 0;
        ProjectSummaryMaterialsSubtotal = 0;
        ProjectSummaryAdjustedServicesTotal = 0;
        ProjectSummaryAdjustedMaterialsTotal = 0;
        ProjectSummaryGrandTotal = 0;
        ProjectSummaryStagesWithPricingCount = 0;
        _summaryTasks = [];
        _summaryStages = [];
        _summaryWorkTypes = [];
        _summaryMaterials = [];
        ProjectSummaryReceiptTaskFilter = null;
        ProjectSummaryReceiptStageFilter = null;
        ProjectSummaryReceiptTaskOptions = [];
        ProjectSummaryReceiptStageOptions = [];
        ProjectSummaryReceiptSelectedTask = null;
        ProjectSummaryReceiptSelectedStage = null;
        ProjectSummaryFilteredServiceSections = [];
        ProjectSummaryFilteredMaterialSections = [];
        ProjectSummaryReceiptServicesGroupByStage = true;
        ProjectSummaryReceiptMaterialsGroupByStage = true;
        ProjectSummaryReceiptFilteredStageCount = 0;
        ProjectSummaryFilteredServicesSubtotal = 0;
        ProjectSummaryFilteredMaterialsSubtotal = 0;
        ProjectSummaryFilteredAdjustedServicesTotal = 0;
        ProjectSummaryFilteredAdjustedMaterialsTotal = 0;
        ProjectSummaryFilteredGrandTotal = 0;
        _goBackAction?.Invoke();
    }

    public void SetProject(LocalProject project, Action? goBackAction = null)
    {
        Project = project;
        _goBackAction = goBackAction;
        BackCommand = goBackAction is null ? null : new RelayCommand(() => goBackAction());
    }

    public async Task LoadAsync()
    {
        if (Project is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var projectEntity = await db.Projects.FindAsync(Project.Id);
        if (projectEntity is not null)
        {
            var managerAv = await db.Users
                .Where(u => u.Id == projectEntity.ManagerId)
                .Select(u => new { u.Name, u.AvatarData, u.AvatarPath })
                .FirstOrDefaultAsync();
            byte[]? mgrAvatarData = null;
            string? mgrAvatarPath = null;
            if (managerAv is not null)
            {
                projectEntity.ManagerName = managerAv.Name;
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

            // Обновляем существующий объект Project на месте для сохранения привязок UI
            Project.Name = projectEntity.Name;
            Project.Description = projectEntity.Description;
            Project.Client = projectEntity.Client;
            Project.Address = projectEntity.Address;
            Project.StartDate = projectEntity.StartDate;
            Project.EndDate = projectEntity.EndDate;
            Project.Status = projectEntity.Status;
            Project.ManagerId = projectEntity.ManagerId;
            Project.ManagerName = projectEntity.ManagerName;
            Project.IsMarkedForDeletion = projectEntity.IsMarkedForDeletion;
            Project.IsArchived = projectEntity.IsArchived;
            Project.IsClosed = projectEntity.IsClosed;
            Project.UpdatedAt = projectEntity.UpdatedAt;
            Project.ManagerAvatarData = mgrAvatarData;
            Project.ManagerAvatarPath = mgrAvatarPath;

            OnPropertyChanged(nameof(Project));
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

        var isClosedProject = Project.IsClosed || Project.Status == ProjectStatus.Closed;
        var tasksQuery = db.Tasks.Where(t => t.ProjectId == Project.Id);
        if (!isClosedProject)
            tasksQuery = tasksQuery.Where(t => !t.IsArchived);

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

        var taskIds = tasks.Select(t => t.Id).ToList();
        var stagesQuery = db.TaskStages.Where(s => taskIds.Contains(s.TaskId));
        if (!isClosedProject)
            stagesQuery = stagesQuery.Where(s => !s.IsArchived);
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

        if (!isClosedProject)
        {
            await RecalcAndSaveTaskStatusesAsync(db, tasks);
            await RecalcProjectStatusAsync(db);
        }

        // Заполняем AssignedUserAvatarData для задач из Users
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
            new() { Label = "Завершено",    Value = CompletedTasksCount,  Color = Color.FromRgb(0x10, 0xB9, 0x81) },
            new() { Label = "В процессе",   Value = InProgressTasksCount, Color = Color.FromRgb(0x3B, 0x82, 0xF6) },
            new() { Label = "Просрочено",   Value = OverdueTasksCount,    Color = Color.FromRgb(0xEF, 0x44, 0x44) },
            new() { Label = "Запланировано",Value = plannedCount,          Color = Color.FromRgb(0x64, 0x74, 0x8B) },
        };

        var activeStages = stages.Where(s => !s.EffectiveMarkedForDeletion).ToList();
        TotalStagesCount = activeStages.Count;
        CompletedStagesCount = activeStages.Count(s => s.Status == StageStatus.Completed);
        InProgressStagesCount = activeStages.Count(s => s.Status == StageStatus.InProgress);
        OverdueStagesCount = activeStages.Count(s => s.IsOverdue);
        int plannedStagesCount = activeStages.Count(s => s.Status == StageStatus.Planned && !s.IsOverdue);

        StageStatsSegments = new List<DonutSegment>
        {
            new() { Label = "Завершено",    Value = CompletedStagesCount,  Color = Color.FromRgb(0x10, 0xB9, 0x81) },
            new() { Label = "В процессе",   Value = InProgressStagesCount, Color = Color.FromRgb(0x3B, 0x82, 0xF6) },
            new() { Label = "Просрочено",   Value = OverdueStagesCount,    Color = Color.FromRgb(0xEF, 0x44, 0x44) },
            new() { Label = "Запланировано",Value = plannedStagesCount,     Color = Color.FromRgb(0x64, 0x74, 0x8B) },
        };

        // Заполняем TaskName и AssignedUserAvatarData для каждого этапа
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

        await LoadProjectPricingSummaryAsync(db, tasks, stages);

        // Построить опции фильтра задач для вкладки "Этапы" проекта
        var taskOpts = new List<TaskFilterOption> { new(null, "Все задачи") };
        taskOpts.AddRange(stages
            .Where(s => s.TaskId != Guid.Empty)
            .GroupBy(s => new { s.TaskId, s.TaskName })
            .OrderBy(g => g.Key.TaskName)
            .Select(g => new TaskFilterOption(g.Key.TaskId, g.Key.TaskName ?? "—")));
        StageTaskFilterOptions = new ObservableCollection<TaskFilterOption>(taskOpts);

        // Инициализируем отфильтрованные этапы на основе текущих фильтров
        ApplyStageFilter();

        // Загружаем файлы
        var projectId = Project!.Id;
        FilesControlVM.Initialize(projectId);

        // Загружаем участников проекта (исполнителей) с AvatarData/AvatarPath из Users
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
                m.SubRole = av.SubRole;
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
        WorkerMembers = [.. members.Where(m => m.UserRole is "Worker" or "Работник")];

        // Загружаем сообщения проекта (только верхнего уровня, без привязки к задаче/этапу) с AvatarData из Users
        var messages = await db.Messages
            .Where(m => m.ProjectId == projectId && m.TaskId == null && m.StageId == null)
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

    private async Task LoadProjectPricingSummaryAsync(
        LocalDbContext db,
        IReadOnlyList<LocalTask> tasks,
        IReadOnlyList<LocalTaskStage> stages)
    {
        var stageIds = stages.Where(s => !s.EffectiveMarkedForDeletion).Select(s => s.Id).ToList();
        if (stageIds.Count == 0)
        {
            ResetProjectSummaryData();
            return;
        }

        var workTypes = await db.StageWorkTypes
            .Where(w => stageIds.Contains(w.StageId))
            .ToListAsync();
        var materials = await db.StageMaterials
            .Where(m => stageIds.Contains(m.StageId))
            .ToListAsync();

        // Load work type templates to get category mapping
        var workTypeTemplateIds = workTypes.Select(w => w.WorkTypeTemplateId).Distinct().ToList();
        var workTypeTemplates = await db.WorkTypeTemplates
            .Where(t => workTypeTemplateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.CategoryName })
            .ToListAsync();
        var categoryByTemplateId = workTypeTemplates.ToDictionary(t => t.Id, t => t.CategoryName);

        _summaryTasks = tasks.ToList();
        _summaryStages = stages.Where(s => !s.EffectiveMarkedForDeletion).ToList();
        _summaryWorkTypes = workTypes;
        _summaryMaterials = materials;

        var result = ProjectPricingSummaryBuilder.Build(tasks, stages, workTypes, materials);

        ProjectSummaryTaskGroups = new ObservableCollection<ProjectSummaryTaskGroupVm>(result.TaskGroups);
        ProjectSummaryServiceLines = new ObservableCollection<ProjectSummaryCatalogLineVm>(result.ServiceLines);
        ProjectSummaryMaterialLines = new ObservableCollection<ProjectSummaryCatalogLineVm>(result.MaterialLines);
        ProjectSummaryServicesSubtotal = result.ServicesSubtotal;
        ProjectSummaryMaterialsSubtotal = result.MaterialsSubtotal;
        ProjectSummaryAdjustedServicesTotal = result.AdjustedServicesTotal;
        ProjectSummaryAdjustedMaterialsTotal = result.AdjustedMaterialsTotal;
        ProjectSummaryGrandTotal = result.AdjustedServicesTotal + result.AdjustedMaterialsTotal;
        ProjectSummarySubtotal = result.ServicesSubtotal + result.MaterialsSubtotal;
        ProjectSummaryStagesWithPricingCount = result.StagesWithPricingCount;

        // Calculate budget by stage status
        var activeStages = _summaryStages.Where(s => !s.EffectiveMarkedForDeletion).ToList();
        var workTypesByStage = _summaryWorkTypes.GroupBy(w => w.StageId).ToDictionary(g => g.Key, g => g.ToList());
        var materialsByStage = _summaryMaterials.GroupBy(m => m.StageId).ToDictionary(g => g.Key, g => g.ToList());

        decimal completedBudget = 0;
        decimal inProgressBudget = 0;
        decimal plannedBudget = 0;

        foreach (var stage in activeStages)
        {
            var svcs = workTypesByStage.GetValueOrDefault(stage.Id) ?? [];
            var mats = materialsByStage.GetValueOrDefault(stage.Id) ?? [];
            var stageServicesSubtotal = svcs.Sum(w => w.Quantity * w.PricePerUnit);
            var stageMaterialsSubtotal = mats.Sum(m => m.Quantity * m.PricePerUnit);
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;
            var stageBudget = stageServicesSubtotal * serviceK + stageMaterialsSubtotal * materialK;

            if (stage.Status == StageStatus.Completed)
                completedBudget += stageBudget;
            else if (stage.Status == StageStatus.InProgress)
                inProgressBudget += stageBudget;
            else if (stage.Status == StageStatus.Planned)
                plannedBudget += stageBudget;
        }

        BudgetByStageStatusSegments = new List<DonutSegment>
        {
            new() { Label = "Завершено", Value = (double)completedBudget, Color = Color.FromRgb(0x10, 0xB9, 0x81) },
            new() { Label = "В работе", Value = (double)inProgressBudget, Color = Color.FromRgb(0x3B, 0x82, 0xF6) },
            new() { Label = "Запланировано", Value = (double)plannedBudget, Color = Color.FromRgb(0x94, 0xA3, 0xB8) },
        };

        // Calculate work type distribution by CategoryName
        var workTypeAgg = new Dictionary<string, decimal>();
        foreach (var wt in _summaryWorkTypes)
        {
            var lineTotal = wt.Quantity * wt.PricePerUnit;
            var stage = _summaryStages.FirstOrDefault(s => s.Id == wt.StageId);
            if (stage is null) continue;
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var adjustedTotal = lineTotal * serviceK;
            var categoryName = categoryByTemplateId.GetValueOrDefault(wt.WorkTypeTemplateId, "Без категории");
            if (!workTypeAgg.ContainsKey(categoryName))
                workTypeAgg[categoryName] = 0m;
            workTypeAgg[categoryName] += adjustedTotal;
        }

        var colors = new[]
        {
            Color.FromRgb(0x3B, 0x82, 0xF6),  // Blue
            Color.FromRgb(0x10, 0xB9, 0x81),  // Green
            Color.FromRgb(0xF5, 0x9E, 0x0B),  // Amber
            Color.FromRgb(0x8B, 0x5C, 0xF6),  // Purple
            Color.FromRgb(0xEC, 0x48, 0x99),  // Pink
            Color.FromRgb(0x06, 0xB6, 0xD4),  // Cyan
            Color.FromRgb(0xF9, 0x71, 0x6C),  // Red
            Color.FromRgb(0x84, 0xCC, 0x16),  // Lime
        };

        var totalWorkTypeBudget = workTypeAgg.Values.Sum();
        var workTypeSegments = workTypeAgg
            .OrderByDescending(p => p.Value)
            .Select((p, i) => new DonutSegment
            {
                Label = p.Key,
                Value = (double)p.Value,
                Color = colors[i % colors.Length],
                Percentage = totalWorkTypeBudget > 0 ? (double)(p.Value / totalWorkTypeBudget * 100m) : 0
            })
            .ToList();

        WorkTypeDistributionSegments = workTypeSegments;

        var taskOpts = new List<TaskFilterOption> { new(null, "Все задачи") };
        taskOpts.AddRange(_summaryTasks
            .OrderBy(t => t.Name)
            .Select(t => new TaskFilterOption(t.Id, t.Name)));
        ProjectSummaryReceiptTaskOptions = new ObservableCollection<TaskFilterOption>(taskOpts);
        ProjectSummaryReceiptSelectedTask = taskOpts[0];
        UpdateProjectSummaryReceiptStageOptions(resetStageSelection: true);
        ApplyProjectSummaryReceiptFilter();
        NotifyProjectSummaryProperties();
    }

    private void ResetProjectSummaryData()
    {
        ProjectSummaryTaskGroups = [];
        ProjectSummaryServiceLines = [];
        ProjectSummaryMaterialLines = [];
        ProjectSummaryServicesSubtotal = 0;
        ProjectSummaryMaterialsSubtotal = 0;
        ProjectSummaryAdjustedServicesTotal = 0;
        ProjectSummaryAdjustedMaterialsTotal = 0;
        ProjectSummaryGrandTotal = 0;
        ProjectSummarySubtotal = 0;
        ProjectSummaryStagesWithPricingCount = 0;
        BudgetByStageStatusSegments = [];
        WorkTypeDistributionSegments = [];
        _summaryTasks = [];
        _summaryStages = [];
        _summaryWorkTypes = [];
        _summaryMaterials = [];
        ProjectSummaryReceiptTaskOptions = [];
        ProjectSummaryReceiptStageOptions = [];
        ProjectSummaryReceiptTaskFilter = null;
        ProjectSummaryReceiptStageFilter = null;
        ProjectSummaryReceiptSelectedTask = null;
        ProjectSummaryReceiptSelectedStage = null;
        ProjectSummaryFilteredServiceSections = [];
        ProjectSummaryFilteredMaterialSections = [];
        ProjectSummaryReceiptServicesGroupByStage = true;
        ProjectSummaryReceiptMaterialsGroupByStage = true;
        ProjectSummaryReceiptFilteredStageCount = 0;
        ProjectSummaryFilteredServicesSubtotal = 0;
        ProjectSummaryFilteredMaterialsSubtotal = 0;
        ProjectSummaryFilteredAdjustedServicesTotal = 0;
        ProjectSummaryFilteredAdjustedMaterialsTotal = 0;
        ProjectSummaryFilteredGrandTotal = 0;
        NotifyProjectSummaryProperties();
    }

    private void UpdateProjectSummaryReceiptStageOptions(bool resetStageSelection = false)
    {
        var opts = new List<StageFilterOption> { new(null, "Все этапы") };
        var query = _summaryStages.AsEnumerable();
        if (ProjectSummaryReceiptTaskFilter.HasValue)
            query = query.Where(s => s.TaskId == ProjectSummaryReceiptTaskFilter.Value);
        opts.AddRange(query
            .OrderBy(s => s.Name)
            .Select(s => new StageFilterOption(s.Id, s.Name)));
        ProjectSummaryReceiptStageOptions = new ObservableCollection<StageFilterOption>(opts);

        if (resetStageSelection)
        {
            ProjectSummaryReceiptSelectedStage = opts[0];
            return;
        }

        var currentId = ProjectSummaryReceiptSelectedStage?.Id;
        ProjectSummaryReceiptSelectedStage =
            opts.FirstOrDefault(o => o.Id == currentId) ?? opts[0];
    }

    private void ApplyProjectSummaryReceiptFilter()
    {
        var receipt = ProjectPricingSummaryBuilder.BuildReceiptRows(
            _summaryStages,
            _summaryWorkTypes,
            _summaryMaterials,
            ProjectSummaryReceiptTaskFilter,
            ProjectSummaryReceiptStageFilter,
            ProjectSummaryReceiptServicesGroupByStage,
            ProjectSummaryReceiptMaterialsGroupByStage);

        ProjectSummaryFilteredServiceSections = new ObservableCollection<ProjectSummaryReceiptStageSectionVm>(receipt.ServiceSections);
        ProjectSummaryFilteredMaterialSections = new ObservableCollection<ProjectSummaryReceiptStageSectionVm>(receipt.MaterialSections);
        ProjectSummaryReceiptFilteredStageCount = receipt.FilteredStageCount;
        ProjectSummaryFilteredServicesSubtotal = receipt.ServicesSubtotal;
        ProjectSummaryFilteredMaterialsSubtotal = receipt.MaterialsSubtotal;
        ProjectSummaryFilteredAdjustedServicesTotal = receipt.AdjustedServicesTotal;
        ProjectSummaryFilteredAdjustedMaterialsTotal = receipt.AdjustedMaterialsTotal;
        ProjectSummaryFilteredGrandTotal = receipt.GrandTotal;

        OnPropertyChanged(nameof(ProjectSummaryFilteredHasServiceAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryFilteredHasMaterialAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryFilteredProgressMaximum));
        OnPropertyChanged(nameof(ProjectSummaryReceiptShowGroupToggle));
        OnPropertyChanged(nameof(ProjectSummaryReceiptServicesGroupButtonText));
        OnPropertyChanged(nameof(ProjectSummaryReceiptMaterialsGroupButtonText));
    }

    [RelayCommand]
    private void ToggleProjectSummaryReceiptServicesGrouping()
    {
        ProjectSummaryReceiptServicesGroupByStage = !ProjectSummaryReceiptServicesGroupByStage;
    }

    [RelayCommand]
    private void ToggleProjectSummaryReceiptMaterialsGrouping()
    {
        ProjectSummaryReceiptMaterialsGroupByStage = !ProjectSummaryReceiptMaterialsGroupByStage;
    }

    public async void OpenProjectSummaryReceiptLinePricing(ReceiptRowVm row)
    {
        if (!CanEditProjectSummaryReceiptRow(row) || !row.StageId.HasValue) return;
        if (MainWindow.Instance is null) return;

        var stageId = row.StageId.Value;
        if (row.IsServiceLine)
        {
            var wt = _summaryWorkTypes.FirstOrDefault(w => w.StageId == stageId && w.WorkTypeTemplateId == row.RowKey);
            if (wt is null) return;

            var basePrice = wt.BasePricePerUnit > 0m ? wt.BasePricePerUnit : wt.PricePerUnit;
            var overlay = new StageLinePricingOverlay(
                wt.WorkTypeName,
                basePrice,
                wt.Quantity,
                wt.PricePerUnit,
                wt.LineAdjustmentPercent,
                new StageLinePricingOptions { Unit = wt.Unit },
                (percent, price, quantity) => ApplyProjectReceiptLineChangesAsync(stageId, row, percent, price, quantity));
            MainWindow.Instance.ShowCenteredOverlay(overlay, 520);
            return;
        }

        var mat = _summaryMaterials.FirstOrDefault(m => m.StageId == stageId && m.MaterialId == row.MaterialId);
        if (mat is null) return;

        var matBasePrice = mat.BasePricePerUnit > 0m ? mat.BasePricePerUnit : mat.PricePerUnit;
        decimal stockAvailable = 0m;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var stock = await db.Materials
                .Where(x => x.Id == mat.MaterialId)
                .Select(x => (decimal?)x.Quantity)
                .FirstOrDefaultAsync();
            if (stock.HasValue)
                stockAvailable = Math.Max(0m, stock.Value) + mat.Quantity;
        }

        var materialOverlay = new StageLinePricingOverlay(
            mat.MaterialName,
            matBasePrice,
            mat.Quantity,
            mat.PricePerUnit,
            mat.LineAdjustmentPercent,
            new StageLinePricingOptions
            {
                IsMaterial = true,
                Unit = mat.Unit,
                StockAvailable = stockAvailable
            },
            (percent, price, quantity) => ApplyProjectReceiptLineChangesAsync(stageId, row, percent, price, quantity));
        MainWindow.Instance.ShowCenteredOverlay(materialOverlay, 520);
    }

    private async Task<bool> ApplyProjectReceiptLineChangesAsync(
        Guid stageId,
        ReceiptRowVm row,
        decimal percent,
        decimal price,
        decimal quantity)
    {
        var taskVm = App.Services.GetRequiredService<TaskDetailViewModel>();
        var stage = _summaryStages.FirstOrDefault(s => s.Id == stageId);
        if (stage is null) return false;

        var task = _summaryTasks.FirstOrDefault(t => t.Id == stage.TaskId);
        if (task is not null)
            taskVm.SetTask(task);

        if (row.IsServiceLine)
        {
            var items = _summaryWorkTypes
                .Where(w => w.StageId == stageId)
                .Select(w => new StageWorkTypeItemRequest(
                    w.WorkTypeTemplateId,
                    w.WorkTypeTemplateId == row.RowKey ? quantity : w.Quantity,
                    w.WorkTypeTemplateId == row.RowKey ? price : w.PricePerUnit,
                    w.WorkTypeName,
                    w.Unit,
                    w.BasePricePerUnit > 0m ? w.BasePricePerUnit : w.PricePerUnit,
                    w.WorkTypeTemplateId == row.RowKey ? percent : w.LineAdjustmentPercent))
                .ToList();
            await taskVm.ReplaceStageWorkTypesAsync(stageId, items);
        }
        else
        {
            var entities = _summaryMaterials
                .Where(m => m.StageId == stageId)
                .Select(m => new LocalStageMaterial
                {
                    Id = Guid.NewGuid(),
                    StageId = stageId,
                    MaterialId = m.MaterialId,
                    MaterialName = m.MaterialName,
                    Unit = m.Unit,
                    Quantity = m.MaterialId == row.MaterialId ? quantity : m.Quantity,
                    PricePerUnit = m.MaterialId == row.MaterialId ? price : m.PricePerUnit,
                    BasePricePerUnit = m.BasePricePerUnit > 0m ? m.BasePricePerUnit : m.PricePerUnit,
                    LineAdjustmentPercent = m.MaterialId == row.MaterialId ? percent : m.LineAdjustmentPercent,
                    IsSynced = false,
                    LastModifiedLocally = DateTime.UtcNow
                })
                .ToList();
            await taskVm.ReplaceStageMaterialsAsync(stageId, entities);
        }

        await ReloadProjectPricingSummaryAsync();
        return true;
    }

    private async Task ReloadProjectPricingSummaryAsync()
    {
        if (Project is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tasks = await db.Tasks.Where(t => t.ProjectId == Project.Id && !t.IsArchived).ToListAsync();
        var stages = await db.TaskStages
            .Where(s => tasks.Select(t => t.Id).Contains(s.TaskId) && !s.IsArchived)
            .ToListAsync();
        await LoadProjectPricingSummaryAsync(db, tasks, stages);
    }

    private void NotifyProjectSummaryProperties()
    {
        OnPropertyChanged(nameof(ProjectSummaryProgressMaximum));
        OnPropertyChanged(nameof(ProjectSummaryHasServiceAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryHasMaterialAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryHasAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryFilteredHasServiceAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryFilteredHasMaterialAdjustment));
        OnPropertyChanged(nameof(ProjectSummaryFilteredProgressMaximum));
        OnPropertyChanged(nameof(ShowProjectSummaryTab));
    }

    private void ApplyTaskFilter()
    {
        var query = Tasks.AsEnumerable();

        if (SearchHelper.Normalize(TaskSearchText) is { } taskTerm)
            query = query.Where(t =>
                SearchHelper.ContainsIgnoreCase(t.Name, taskTerm) ||
                SearchHelper.ContainsIgnoreCase(t.Description, taskTerm));

        if (TaskStatusFilter == "Пометка удаления")
        {
            query = query.Where(t => t.EffectiveTaskMarkedForDeletion);
        }
        else if (TaskStatusFilter != "Все")
        {
            var status = TaskStatusFilter switch
            {
                "Запланирована" => TaskStatus.Planned,
                "Выполняется" => TaskStatus.InProgress,
                "Завершена" => TaskStatus.Completed,
                _ => (TaskStatus?)null
            };
            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value && !t.EffectiveTaskMarkedForDeletion);
        }

        if (TaskPriorityFilter != "Все")
        {
            var priority = TaskPriorityFilter switch
            {
                "Низкий" => TaskPriority.Low,
                "Средний" => TaskPriority.Medium,
                "Высокий" => TaskPriority.High,
                "Критический" => TaskPriority.Critical,
                _ => (TaskPriority?)null
            };
            if (priority.HasValue)
                query = query.Where(t => t.Priority == priority.Value);
        }

        var list = query
            .OrderBy(t => t.EffectiveTaskMarkedForDeletion)
            .ThenBy(t => t.Status switch
            {
                TaskStatus.Planned => 0,
                TaskStatus.InProgress => 1,
                TaskStatus.Paused => 2,
                TaskStatus.Completed => 3,
                _ => 4
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

        if (StageStatusFilter == "Пометка удаления")
        {
            query = query.Where(s => s.EffectiveMarkedForDeletion);
        }
        else if (StageStatusFilter != "Все статусы")
        {
            var targetStatus = StageStatusFilter switch
            {
                "Запланирован" => StageStatus.Planned,
                "Выполняется" => StageStatus.InProgress,
                "Завершён" => StageStatus.Completed,
                _ => (StageStatus?)null
            };
            if (targetStatus.HasValue)
                query = query.Where(s => s.Status == targetStatus.Value && !s.EffectiveMarkedForDeletion);
        }

        static int StageStatusOrder(StageStatus st) => st switch
        {
            StageStatus.Planned => 0,
            StageStatus.InProgress => 1,
            StageStatus.Completed => 2,
            _ => 9
        };

        var ordered = query
            .OrderBy(s => s.EffectiveMarkedForDeletion)
            .ThenBy(s => StageStatusOrder(s.Status))
            .ThenBy(s => s.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.TaskName)
            .ThenBy(s => s.Name)
            .ToList();

        FilteredStages = new ObservableCollection<LocalTaskStage>(ordered);

        StageItem MakeStageItem(LocalTaskStage s) => new()
        {
            Stage = s,
            TaskId = s.TaskId,
            TaskName = s.TaskName,
            ProjectId = Project?.Id ?? Guid.Empty,
            ProjectName = Project?.Name ?? "—"
        };

        var markedItems = ordered.Where(s => s.EffectiveMarkedForDeletion).Select(MakeStageItem).ToList();
        var stageItems = ordered.Where(s => !s.EffectiveMarkedForDeletion).Select(MakeStageItem).ToList();

        FilteredMarkedStages = new ObservableCollection<StageItem>(markedItems);
        FilteredPlannedStages = new ObservableCollection<StageItem>(stageItems.Where(s => s.Stage.Status == StageStatus.Planned));
        FilteredInProgressStages = new ObservableCollection<StageItem>(stageItems.Where(s => s.Stage.Status == StageStatus.InProgress));
        FilteredCompletedStages = new ObservableCollection<StageItem>(stageItems.Where(s => s.Stage.Status == StageStatus.Completed));

        var taskGroups = ordered
            .GroupBy(s => new { s.TaskId, s.TaskName })
            .OrderBy(g => g.Key.TaskName)
            .Select(g => new TaskStageGroup(g.Key.TaskId, g.Key.TaskName ?? "—", Project?.Id ?? Guid.Empty, Project?.Name ?? "—",
                g.OrderBy(s => s.EffectiveMarkedForDeletion)
                    .ThenBy(s => StageStatusOrder(s.Status))
                    .ThenBy(s => s.DueDate ?? DateOnly.MaxValue)
                    .ThenByDescending(s => s.UpdatedAt)
                    .Select(MakeStageItem)
                    .ToList(), isFirstInProject: false))
            .ToList();

        // Для одного проекта всегда одна группа проекта
        var projectName = Project?.Name ?? "—";
        var withFirst = taskGroups.Select((tg, i) => new TaskStageGroup(
            tg.TaskId, tg.TaskName, tg.ProjectId, tg.ProjectName, tg.Stages, i == 0)).ToList();
        var projectGroup = new ProjectStageGroup(Project?.Id ?? Guid.Empty, projectName, withFirst);
        ProjectStageGroups = new ObservableCollection<ProjectStageGroup> { projectGroup };
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
        // Статус вычисляется автоматически, не переопределять из запроса
        project.ManagerId = req.ManagerId;
        project.ManagerName = managerName;
        project.IsMarkedForDeletion = req.IsMarkedForDeletion;
        project.IsArchived = req.IsArchived;
        project.IsClosed = req.IsClosed;
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
    private void SwitchStageView(string mode)
    {
        StageViewMode = mode;
        _settings.SetValue("StagesViewMode", mode);
    }

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
        var details = ActivityDetailsService.BuildTaskUpdateDetails(task, req, assignedName, includeStatus: false);

        task.Name = req.Name;
        task.Description = req.Description;
        task.AssignedUserId = req.AssignedUserId;
        task.AssignedUserName = assignedName;
        task.Priority = req.Priority;
        task.DueDate = req.DueDate;
        // Статус вычисляется автоматически из этапов, не устанавливать из запроса
        var stages = await db.TaskStages.Where(s => s.TaskId == id).ToListAsync();
        task.TotalStages = stages.Count;
        task.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);
        task.InProgressStages = stages.Count(s => s.Status == StageStatus.InProgress);
        var oldStatus = task.Status;
        if (stages.Count > 0)
            task.Status = StatusCalculator.GetTaskStatusFromStages(stages);
        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await RecalcProjectStatusAsync(db);
        var syncTaskReq = req with { IsMarkedForDeletion = task.IsMarkedForDeletion, IsArchived = task.IsArchived };
        await _sync.QueueOperationAsync("Task", id, SyncOperation.Update, syncTaskReq);
        await LogActivityAsync(db, $"Обновлена задача «{req.Name}»", "Task", id, ActivityActionKind.Updated, details);

        // Логируем изменение статуса задачи отдельно
        if (oldStatus != task.Status)
        {
            var statusText = task.Status switch
            {
                TaskStatus.Planned => "Запланирована",
                TaskStatus.InProgress => "В процессе",
                TaskStatus.Completed => "Завершена",
                TaskStatus.Paused => "Приостановлена",
                _ => task.Status.ToString()
            };
            await LogActivityAsync(db, $"Статус задачи «{task.Name}» изменён на {statusText}", "Task", id, ActivityActionKind.TaskStatusChanged);
        }

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

        // Каскадный архив для всех этапов этой задачи
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

        // Ставим обновления в очередь без await, чтобы избежать блокировки
        _ = _sync.QueueOperationAsync("Task", entity.Id, SyncOperation.Update, SyncPayloads.Task(entity));
        foreach (var s in stages)
            _ = _sync.QueueOperationAsync("Stage", s.Id, SyncOperation.Update, SyncPayloads.Stage(s));
        await LogActivityAsync(db, $"Задача «{task.Name}» перемещена в архив", "Task", task.Id, ActivityActionKind.Deleted);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MarkTaskForDeletionAsync(LocalTask task)
    {
        if (!CanMarkTaskDeletion()) return;
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

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        await CloseProjectAsync(null);
    }

    public async Task CloseProjectAsync(string? closureReason)
    {
        if (Project is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(Project.Id);
        if (project is null) return;

        project.IsClosed = true;
        project.Status = ProjectStatus.Closed;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        project.ClosedAt = DateTime.UtcNow;
        project.ClosureReason = string.IsNullOrWhiteSpace(closureReason) ? null : closureReason;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Project", project.Id, SyncOperation.Update, SyncPayloads.Project(project));

        await LogActivityAsync(db, $"Проект «{project.Name}» закрыт", "Project", project.Id, ActivityActionKind.Updated);

        // Навигация назад
        _goBackAction?.Invoke();
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
            UserColor = "#0F2038",
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
            new CreateDiscussionMessageRequest(msg.Id, msg.TaskId, msg.ProjectId, null, msg.Text, msg.CreatedAt));

        await LogActivityAsync(db, $"Сообщение в обсуждении проекта «{Project.Name}»", "Message", msg.Id, ActivityActionKind.Message);
        Messages.Add(msg);
    }

    private async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId, string? actionType = null, string? detailsText = null)
    {
        var userName = _auth.UserName ?? "Система";
        var userId = _auth.UserId;
        var actorRole = _auth.UserRole;
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
            UserColor = "#0F2038",
            ActionType = actionType,
            ActionText = actionText,
            DetailsText = detailsText ?? ActivityDetailsService.BuildGenericDetails(actionText, entityType, actionType),
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        await _sync.QueueLocalActivityLogAsync(log);
    }

    /// <summary>Сохраняет статус задач/этапов и пересчитывает статус проекта.</summary>
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

    private async Task RecalcProjectStatusAsync(LocalDbContext db)
    {
        if (Project is null) return;
        var project = await db.Projects.FindAsync(Project.Id);
        if (project is null) return;

        var oldStatus = project.Status;

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

        // Логируем изменение статуса проекта
        if (oldStatus != project.Status)
        {
            var statusText = project.Status switch
            {
                ProjectStatus.Planning => "Запланирован",
                ProjectStatus.InProgress => "Выполняется",
                ProjectStatus.Completed => "Завершён",
                ProjectStatus.Cancelled => "Отменён",
                ProjectStatus.Closed => "Закрыт",
                _ => project.Status.ToString()
            };
            db.ActivityLogs.Add(new LocalActivityLog
            {
                Id = Guid.NewGuid(),
                UserId = null, // Системное действие - без аватарки
                ActorRole = "System",
                ActionType = ActivityActionKind.StatusChanged,
                ActionText = $"Статус проекта «{project.Name}» изменён на {statusText}",
                EntityType = "Project",
                EntityId = project.Id,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

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
