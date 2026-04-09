using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.Views.Overlays;

namespace MPMS.ViewModels;

/// <summary>Редактор этапа (встроенная страница): вкладки, услуги, работники, материалы, сводка.</summary>
public partial class StageEditViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IServiceProvider _sp;

    private LocalTask? _task;
    private LocalTaskStage? _editStage;
    private Action? _goBack;
    private Func<Task>? _onSavedAsync;
    private readonly HashSet<Guid> _selectedAssigneeIds = [];
    private List<AssigneePickerItem> _workerAssigneeItems = [];

    [ObservableProperty] private string _pageTitle = "Добавить этап";
    [ObservableProperty] private string _saveButtonText = "Добавить этап";
    [ObservableProperty] private string _contextSubtitle = "";
    [ObservableProperty] private string _projectNameReadOnly = "";
    [ObservableProperty] private string _stageName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private string _activeTab = "Main";

    [ObservableProperty] private bool _showProjectTaskPickers;
    [ObservableProperty] private bool _showProjectNameRow;
    /// <summary>Ложь, если проект уже выбран (добавление этапа из карточки проекта).</summary>
    [ObservableProperty] private bool _showProjectPickerList = true;
    [ObservableProperty] private bool _showAssigneesSection = true;
    [ObservableProperty] private bool _showWorkerAutoHint;

    [ObservableProperty] private ObservableCollection<PickerRowVm> _projectRows = [];
    [ObservableProperty] private ObservableCollection<PickerRowVm> _taskRows = [];
    [ObservableProperty] private Guid? _selectedProjectId;
    [ObservableProperty] private Guid? _selectedTaskId;

    [ObservableProperty] private string _serviceSearchText = "";
    [ObservableProperty] private string _serviceCategoryFilter = "Все категории";
    [ObservableProperty] private ObservableCollection<string> _serviceCategoryOptions = [];
    [ObservableProperty] private ObservableCollection<LocalServiceTemplate> _serviceCatalogFiltered = [];
    private List<LocalServiceTemplate> _allServiceTemplates = [];

    [ObservableProperty] private ObservableCollection<StageServiceLineVm> _selectedServices = [];
    [ObservableProperty] private ObservableCollection<StageMaterialLineVm> _materialLines = [];
    [ObservableProperty] private ObservableCollection<LocalMaterial> _materialCatalog = [];

    [ObservableProperty] private ObservableCollection<AssigneePickerItem> _workerPickerItems = [];

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private decimal _summaryServicesTotal;
    [ObservableProperty] private decimal _summaryMaterialsTotal;
    [ObservableProperty] private decimal _summaryGrandTotal;

    public StageEditViewModel(
        IDbContextFactory<LocalDbContext> dbFactory,
        IAuthService auth,
        IServiceProvider sp)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _sp = sp;

        SelectedServices.CollectionChanged += OnTotalsCollectionChanged;
        MaterialLines.CollectionChanged += OnTotalsCollectionChanged;
    }

    private void OnTotalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged n)
                    n.PropertyChanged += OnLinePropertyChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged n)
                    n.PropertyChanged -= OnLinePropertyChanged;
            }
        }
        RecalculateTotals();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageServiceLineVm.LineTotal) or nameof(StageMaterialLineVm.LineTotal))
            RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        SummaryServicesTotal = SelectedServices.Sum(s => s.LineTotal);
        SummaryMaterialsTotal = MaterialLines.Sum(m => m.LineTotal);
        SummaryGrandTotal = SummaryServicesTotal + SummaryMaterialsTotal;
    }

    public void SetCreateForTask(LocalTask task, Action goBack, Func<Task>? onSavedAsync = null)
    {
        Reset();
        _task = task;
        _editStage = null;
        _goBack = goBack;
        _onSavedAsync = onSavedAsync;
        PageTitle = "Добавить этап";
        SaveButtonText = "Добавить этап";
        ContextSubtitle = $"Задача: {task.Name}";
        ProjectNameReadOnly = task.ProjectName ?? "—";
        ShowProjectTaskPickers = false;
        ShowProjectNameRow = true;
        ShowProjectPickerList = true;
        ShowWorkerAutoHint = IsWorker();
        ShowAssigneesSection = !IsWorker();
        ApplyWorkerDefaultSelection(task.Id);
        _ = LoadAssigneesAsync(task.Id);
    }

    public void SetCreateForProject(Guid projectId, Action goBack, Func<Task>? onSavedAsync = null)
    {
        Reset();
        _goBack = goBack;
        _onSavedAsync = onSavedAsync;
        PageTitle = "Добавить этап";
        SaveButtonText = "Добавить этап";
        ContextSubtitle = "Выберите задачу";
        ShowProjectTaskPickers = true;
        ShowProjectNameRow = true;
        ShowProjectPickerList = false;
        ShowWorkerAutoHint = IsWorker();
        ShowAssigneesSection = !IsWorker();
        _ = LoadProjectNameAsync(projectId);
        _ = LoadTasksForProjectAsync(projectId);
    }

    public void SetCreateFromStagesPage(Action goBack, Func<Task>? onSavedAsync = null)
    {
        Reset();
        _goBack = goBack;
        _onSavedAsync = onSavedAsync;
        PageTitle = "Добавить этап";
        SaveButtonText = "Добавить этап";
        ContextSubtitle = "Выберите проект и задачу";
        ShowProjectTaskPickers = true;
        ShowProjectNameRow = false;
        ShowProjectPickerList = true;
        ShowWorkerAutoHint = IsWorker();
        ShowAssigneesSection = !IsWorker();
        _ = LoadProjectsForPickerAsync();
    }

    public void SetEditMode(LocalTaskStage stage, LocalTask task, Action goBack, Func<Task>? onSavedAsync = null)
    {
        Reset();
        _editStage = stage;
        _task = task;
        _goBack = goBack;
        _onSavedAsync = onSavedAsync;
        PageTitle = "Редактировать этап";
        SaveButtonText = "Сохранить";
        ContextSubtitle = $"Задача: {task.Name}";
        ProjectNameReadOnly = task.ProjectName ?? "—";
        StageName = stage.Name;
        Description = stage.Description ?? "";
        DueDate = stage.DueDate?.ToDateTime(TimeOnly.MinValue);
        ShowProjectTaskPickers = false;
        ShowProjectNameRow = true;
        ShowProjectPickerList = true;
        ShowWorkerAutoHint = false;
        ShowAssigneesSection = !IsWorker();
        _ = LoadAssigneesAsync(task.Id, stage.Id);
        _ = LoadExistingServicesAndMaterialsAsync(stage.Id);
    }

    private void Reset()
    {
        _task = null;
        _editStage = null;
        _goBack = null;
        _onSavedAsync = null;
        _selectedAssigneeIds.Clear();
        _workerAssigneeItems = [];
        WorkerPickerItems = [];
        StageName = "";
        Description = "";
        DueDate = null;
        ActiveTab = "Main";
        ServiceSearchText = "";
        ServiceCategoryFilter = "Все категории";
        SelectedServices.Clear();
        MaterialLines.Clear();
        ErrorMessage = null;
        SelectedProjectId = null;
        SelectedTaskId = null;
        ProjectRows = [];
        TaskRows = [];
        ShowProjectPickerList = true;
    }

    private async Task LoadProjectNameAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var p = await db.Projects.FindAsync(projectId);
        ProjectNameReadOnly = p?.Name ?? "—";
    }

    private void ApplyWorkerDefaultSelection(Guid taskId)
    {
        if (!IsWorker() || !_auth.UserId.HasValue) return;
        _selectedAssigneeIds.Clear();
        _selectedAssigneeIds.Add(_auth.UserId.Value);
    }

    private bool IsWorker() =>
        string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

    public async Task LoadAsync()
    {
        await LoadServiceCatalogAsync();
        await LoadMaterialCatalogAsync();
        RecalculateTotals();
    }

    private async Task LoadExistingServicesAndMaterialsAsync(Guid stageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var svcs = await db.StageServices
            .Where(s => s.StageId == stageId)
            .OrderBy(s => s.ServiceName)
            .ToListAsync();
        foreach (var s in svcs)
        {
            var line = new StageServiceLineVm(s.ServiceTemplateId, s.ServiceName, s.Unit, s.PricePerUnit)
            {
                Quantity = s.Quantity
            };
            SelectedServices.Add(line);
        }

        var mats = await db.StageMaterials
            .Where(m => m.StageId == stageId)
            .OrderBy(m => m.MaterialName)
            .ToListAsync();
        foreach (var m in mats)
        {
            var line = new StageMaterialLineVm(m.MaterialId, m.MaterialName, m.Unit, m.PricePerUnit)
            {
                Quantity = m.Quantity
            };
            MaterialLines.Add(line);
        }
    }

    private async Task LoadServiceCatalogAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        _allServiceTemplates = await db.ServiceTemplates
            .Where(t => t.IsActive)
            .OrderBy(t => t.CategoryName)
            .ThenBy(t => t.Name)
            .ToListAsync();
        var cats = _allServiceTemplates.Select(t => t.CategoryName).Distinct().OrderBy(x => x).ToList();
        ServiceCategoryOptions = new ObservableCollection<string>(["Все категории", .. cats]);
        ApplyServiceFilters();
    }

    partial void OnServiceSearchTextChanged(string value) => ApplyServiceFilters();
    partial void OnServiceCategoryFilterChanged(string value) => ApplyServiceFilters();

    partial void OnSelectedProjectIdChanged(Guid? value)
    {
        if (value is { } pid)
        {
            RefreshPickerSelection(ProjectRows, pid);
            _ = LoadTasksForProjectAsync(pid);
        }
    }

    partial void OnSelectedTaskIdChanged(Guid? value)
    {
        if (value is { } tid)
        {
            RefreshPickerSelection(TaskRows, tid);
            _ = LoadAssigneesAsync(tid);
        }
    }

    private void ApplyServiceFilters()
    {
        IEnumerable<LocalServiceTemplate> q = _allServiceTemplates;
        if (!string.IsNullOrWhiteSpace(ServiceCategoryFilter) && ServiceCategoryFilter != "Все категории")
            q = q.Where(t => t.CategoryName == ServiceCategoryFilter);
        var s = ServiceSearchText.Trim();
        if (s.Length > 0)
            q = q.Where(t => t.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                             || (t.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (t.Article?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        ServiceCatalogFiltered = new ObservableCollection<LocalServiceTemplate>(q.ToList());
    }

    private async Task LoadMaterialCatalogAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var mats = await db.Materials
            .Where(m => !m.IsWrittenOff)
            .OrderBy(m => m.Name)
            .ToListAsync();
        MaterialCatalog = new ObservableCollection<LocalMaterial>(mats);
    }

    private async Task LoadProjectsForPickerAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var projects = await db.Projects
            .Where(p => !p.IsArchived && !p.IsMarkedForDeletion)
            .OrderBy(p => p.Name)
            .ToListAsync();
        ProjectRows = new ObservableCollection<PickerRowVm>(projects.Select(p => new PickerRowVm(p.Id, p.Name)));
        if (projects.Count > 0)
            SelectedProjectId = projects[0].Id;
    }

    private static void RefreshPickerSelection(ObservableCollection<PickerRowVm> rows, Guid selectedId)
    {
        foreach (var r in rows)
            r.IsSelected = r.Id == selectedId;
    }

    private async Task LoadTasksForProjectAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tasks = await db.Tasks
            .Where(t => t.ProjectId == projectId && !t.IsArchived && !t.IsMarkedForDeletion)
            .OrderBy(t => t.Name)
            .ToListAsync();
        TaskRows = new ObservableCollection<PickerRowVm>(tasks.Select(t => new PickerRowVm(t.Id, t.Name)));
        if (tasks.Count > 0)
            SelectedTaskId = tasks[0].Id;
        else
        {
            SelectedTaskId = null;
            _selectedAssigneeIds.Clear();
            WorkerPickerItems = [];
        }
    }

    private async Task LoadAssigneesAsync(Guid taskId, Guid? stageId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var blockedUserIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
        var taskAssignees = await db.TaskAssignees
            .Where(ta => ta.TaskId == taskId && !blockedUserIds.Contains(ta.UserId))
            .OrderBy(ta => ta.UserName)
            .ToListAsync();

        var taskEntity = await db.Tasks.FindAsync(taskId);
        if (taskAssignees.Count == 0 && taskEntity?.AssignedUserId is { } legacyId
                                       && !blockedUserIds.Contains(legacyId))
        {
            taskAssignees.Add(new LocalTaskAssignee
            {
                TaskId = taskId,
                UserId = legacyId,
                UserName = taskEntity.AssignedUserName ?? "—"
            });
        }

        if (IsWorker() && !stageId.HasValue && _auth.UserId.HasValue)
        {
            _selectedAssigneeIds.Clear();
            _selectedAssigneeIds.Add(_auth.UserId.Value);
            if (taskAssignees.All(ta => ta.UserId != _auth.UserId.Value))
            {
                var self = await db.Users.FindAsync(_auth.UserId.Value);
                taskAssignees.Insert(0, new LocalTaskAssignee
                {
                    TaskId = taskId,
                    UserId = _auth.UserId.Value,
                    UserName = self?.Name ?? _auth.UserName ?? "—"
                });
            }
        }

        var userIds = taskAssignees.Select(ta => ta.UserId).Distinct().ToList();
        var userRows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarPath, u.AvatarData, u.RoleName, u.SubRole, u.AdditionalSubRoles })
            .ToDictionaryAsync(u => u.Id);
        foreach (var ta in taskAssignees)
        {
            if (userRows.TryGetValue(ta.UserId, out var ur))
            {
                ta.AvatarPath = ur.AvatarPath;
                ta.AvatarData = ur.AvatarData;
            }
        }

        if (stageId.HasValue)
        {
            var stageAssignees = await db.StageAssignees
                .Where(sa => sa.StageId == stageId.Value && !blockedUserIds.Contains(sa.UserId))
                .ToListAsync();
            foreach (var sa in stageAssignees)
                _selectedAssigneeIds.Add(sa.UserId);

            var stageEntity = await db.TaskStages.FindAsync(stageId.Value);
            if (stageEntity?.AssignedUserId is { } aid && !blockedUserIds.Contains(aid)
                                                        && !_selectedAssigneeIds.Contains(aid))
                _selectedAssigneeIds.Add(aid);
        }

        _workerAssigneeItems = taskAssignees.Select(ta =>
        {
            userRows.TryGetValue(ta.UserId, out var ur);
            var role = string.IsNullOrWhiteSpace(ur?.RoleName) ? "Worker" : ur.RoleName;
            return new AssigneePickerItem(
                ta.UserId,
                ta.UserName,
                role,
                _selectedAssigneeIds,
                ta.AvatarPath,
                ta.AvatarData,
                ur?.SubRole,
                ur?.AdditionalSubRoles);
        }).ToList();

        _workerAssigneeItems = _workerAssigneeItems.Where(i => i.RoleDisplay == "Работник").ToList();
        var workerIds = _workerAssigneeItems.Select(i => i.UserId).ToHashSet();
        _selectedAssigneeIds.RemoveWhere(id => !workerIds.Contains(id));

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var i in _workerAssigneeItems)
                i.RefreshSelected(_selectedAssigneeIds);
            WorkerPickerItems = new ObservableCollection<AssigneePickerItem>(_workerAssigneeItems);
        });
    }

    [RelayCommand]
    private void GoBack() => _goBack?.Invoke();

    [RelayCommand]
    public void ToggleAssignee(AssigneePickerItem? item)
    {
        if (item is null) return;
        if (_selectedAssigneeIds.Contains(item.UserId))
        {
            if (!IsWorker() && _selectedAssigneeIds.Count <= 1)
            {
                ErrorMessage = "На этапе должен остаться хотя бы один работник.";
                return;
            }
            _selectedAssigneeIds.Remove(item.UserId);
        }
        else
        {
            _selectedAssigneeIds.Add(item.UserId);
            ErrorMessage = null;
        }
        foreach (var i in _workerAssigneeItems)
            i.RefreshSelected(_selectedAssigneeIds);
    }

    [RelayCommand]
    private void AddServiceTemplate(LocalServiceTemplate? tpl)
    {
        if (tpl is null) return;
        var line = new StageServiceLineVm(tpl.Id, tpl.Name, tpl.Unit, tpl.BasePrice)
        {
            Quantity = 1
        };
        SelectedServices.Add(line);
    }

    [RelayCommand]
    private void RemoveServiceLine(StageServiceLineVm? line)
    {
        if (line is null) return;
        SelectedServices.Remove(line);
    }

    [RelayCommand]
    private void AddMaterialRow()
    {
        MaterialLines.Add(new StageMaterialLineVm { Quantity = 1 });
    }

    public void AdjustServiceQuantity(StageServiceLineVm line, decimal delta)
    {
        var n = Math.Round(line.Quantity + delta, 2, MidpointRounding.AwayFromZero);
        line.Quantity = Math.Max(0.01m, n);
    }

    public void AdjustMaterialQuantity(StageMaterialLineVm line, decimal delta)
    {
        var n = Math.Round(line.Quantity + delta, 2, MidpointRounding.AwayFromZero);
        line.Quantity = Math.Max(0.01m, n);
    }

    [RelayCommand]
    private void SelectProjectRow(PickerRowVm? row)
    {
        if (row is null) return;
        SelectedProjectId = row.Id;
    }

    [RelayCommand]
    private void SelectTaskRow(PickerRowVm? row)
    {
        if (row is null) return;
        SelectedTaskId = row.Id;
    }

    public void ApplyMaterialToLine(StageMaterialLineVm line, LocalMaterial? mat)
    {
        if (mat is null) return;
        line.ApplyFrom(mat);
        RecalculateTotals();
    }

    [RelayCommand]
    private void RemoveMaterialLine(StageMaterialLineVm? line)
    {
        if (line is null) return;
        MaterialLines.Remove(line);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(StageName))
        {
            ErrorMessage = "Введите название этапа.";
            return;
        }

        Guid taskId;
        TaskDetailViewModel taskVm = _sp.GetRequiredService<TaskDetailViewModel>();

        if (ShowProjectTaskPickers)
        {
            if (SelectedTaskId is not Guid tid)
            {
                ErrorMessage = "Выберите задачу.";
                return;
            }
            taskId = tid;
            var t = await GetTaskByIdAsync(taskId);
            if (t is null)
            {
                ErrorMessage = "Задача не найдена.";
                return;
            }
            taskVm.SetTask(t);
            _task = t;
        }
        else
        {
            if (_task is null)
            {
                ErrorMessage = "Задача не выбрана.";
                return;
            }
            taskVm.SetTask(_task);
            taskId = _task.Id;
        }

        if (!IsWorker() && _selectedAssigneeIds.Count == 0)
        {
            ErrorMessage = "Назначьте хотя бы одного работника на этап.";
            return;
        }

        if (IsWorker() && _editStage is null && _auth.UserId.HasValue)
        {
            _selectedAssigneeIds.Clear();
            _selectedAssigneeIds.Add(_auth.UserId.Value);
        }

        Guid? primaryAssigneeId = _workerAssigneeItems
            .Select(i => i.UserId)
            .FirstOrDefault(id => _selectedAssigneeIds.Contains(id));
        if (primaryAssigneeId == Guid.Empty)
            primaryAssigneeId = null;

        DateOnly? dueDate = DueDate is { } sd ? DateOnly.FromDateTime(sd) : null;
        if (!DueDatePolicy.IsAllowed(dueDate))
        {
            ErrorMessage = DueDatePolicy.PastNotAllowedMessage;
            return;
        }

        foreach (var ml in MaterialLines)
        {
            if (ml.MaterialId == Guid.Empty)
            {
                ErrorMessage = "Укажите материал во всех строках или удалите пустые.";
                return;
            }
        }

        var serviceItems = SelectedServices
            .Select(s => new StageServiceItemRequest(s.TemplateId, s.Quantity, s.PricePerUnit))
            .ToList();

        IsBusy = true;
        try
        {
            Guid stageId;
            if (_editStage is null)
            {
                var localId = Guid.NewGuid();
                var req = new CreateStageRequest(
                    taskId,
                    StageName.Trim(),
                    string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    primaryAssigneeId,
                    dueDate,
                    null,
                    null,
                    0,
                    null,
                    serviceItems);
                await taskVm.SaveNewStageAsync(req, localId);
                stageId = localId;
            }
            else
            {
                var req = new UpdateStageRequest(
                    StageName.Trim(),
                    string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    primaryAssigneeId,
                    _editStage.Status,
                    dueDate,
                    _editStage.IsMarkedForDeletion,
                    _editStage.IsArchived,
                    null,
                    0,
                    0,
                    serviceItems);
                await taskVm.SaveUpdatedStageAsync(_editStage.Id, req);
                stageId = _editStage.Id;
            }

            var assigneeRows = _selectedAssigneeIds
                .Select(uid =>
                {
                    var item = _workerAssigneeItems.FirstOrDefault(i => i.UserId == uid);
                    return (uid, item?.Name ?? "—");
                })
                .ToList();
            await taskVm.ReplaceStageAssigneesAsync(stageId, assigneeRows);

            var matEntities = MaterialLines.Select(m => new LocalStageMaterial
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                MaterialId = m.MaterialId,
                MaterialName = m.MaterialName,
                Unit = m.Unit,
                Quantity = m.Quantity,
                PricePerUnit = m.PricePerUnit,
                IsSynced = false,
                LastModifiedLocally = DateTime.UtcNow
            }).ToList();
            await taskVm.ReplaceStageMaterialsAsync(stageId, matEntities);

            if (_onSavedAsync is not null)
                await _onSavedAsync();
            _goBack?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<LocalTask?> GetTaskByIdAsync(Guid taskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks.FindAsync(taskId);
    }
}

/// <summary>Строка списка выбора проекта/задачи.</summary>
public sealed partial class PickerRowVm : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }

    [ObservableProperty] private bool _isSelected;

    public PickerRowVm(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}
