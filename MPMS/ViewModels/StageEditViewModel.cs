using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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
    private CancellationTokenSource? _errorMessageCts;
    private Guid? _peekProjectId;

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

    [ObservableProperty] private string _materialSearchText = "";
    [ObservableProperty] private string _materialCategoryFilter = "Все категории";
    [ObservableProperty] private ObservableCollection<string> _materialCategoryOptions = [];
    [ObservableProperty] private string _equipmentSearchText = "";
    [ObservableProperty] private string _equipmentCategoryFilter = "Все категории";
    [ObservableProperty] private ObservableCollection<string> _equipmentCategoryOptions = [];
    [ObservableProperty] private ObservableCollection<StageServiceLineVm> _selectedServices = [];
    [ObservableProperty] private ObservableCollection<StageMaterialLineVm> _materialLines = [];
    [ObservableProperty] private ObservableCollection<StageEquipmentLineVm> _equipmentLines = [];
    [ObservableProperty] private ObservableCollection<LocalMaterial> _materialCatalog = [];
    [ObservableProperty] private ObservableCollection<LocalMaterial> _materialCatalogFiltered = [];
    private List<LocalMaterial> _allMaterialTemplates = [];
    [ObservableProperty] private ObservableCollection<LocalEquipment> _equipmentCatalogFiltered = [];
    private List<LocalEquipment> _allEquipmentTemplates = [];

    [ObservableProperty] private ObservableCollection<AssigneePickerItem> _workerPickerItems = [];
    [ObservableProperty] private string _workerSearchText = "";
    [ObservableProperty] private string _workerSpecialtyFilter = "Все специальности";
    [ObservableProperty] private ObservableCollection<string> _workerSpecialtyOptions = [];

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private decimal _summaryServicesTotal;
    [ObservableProperty] private decimal _summaryMaterialsTotal;
    [ObservableProperty] private decimal _summaryGrandTotal;
    [ObservableProperty] private decimal _serviceAdjustmentPercent;
    [ObservableProperty] private decimal _materialAdjustmentPercent;
    [ObservableProperty] private ObservableCollection<ReceiptRowVm> _serviceReceiptRows = [];
    [ObservableProperty] private ObservableCollection<ReceiptRowVm> _materialReceiptRows = [];
    public decimal AdjustedServicesTotal => SummaryServicesTotal * (1m + ServiceAdjustmentPercent / 100m);
    public decimal AdjustedMaterialsTotal => SummaryMaterialsTotal * (1m + MaterialAdjustmentPercent / 100m);
    public decimal AdjustedGrandTotal => AdjustedServicesTotal + AdjustedMaterialsTotal;
    public int ServicesCount => SelectedServices.Count;
    public int MaterialsCount => MaterialLines.Count;
    public decimal ServicesQuantityTotal => SelectedServices.Sum(s => s.Quantity);
    public decimal MaterialsQuantityTotal => MaterialLines.Sum(m => m.Quantity);
    public decimal AverageServicePrice => ServicesQuantityTotal > 0 ? SummaryServicesTotal / ServicesQuantityTotal : 0;
    public decimal AverageMaterialPrice => MaterialsQuantityTotal > 0 ? SummaryMaterialsTotal / MaterialsQuantityTotal : 0;
    public int SelectedWorkersCount => _selectedAssigneeIds.Count;
    public int AvailableWorkersCount => WorkerPickerItems.Count;
    public Guid? PeekProjectId => _peekProjectId;

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
        EquipmentLines.CollectionChanged += (_, _) => ApplyEquipmentFilters();
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
        if (ReferenceEquals(sender, SelectedServices))
            ApplyServiceFilters();
        else if (ReferenceEquals(sender, MaterialLines))
            ApplyMaterialFilters();
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
        BuildReceiptRows();
        OnPropertyChanged(nameof(ServicesCount));
        OnPropertyChanged(nameof(MaterialsCount));
        OnPropertyChanged(nameof(ServicesQuantityTotal));
        OnPropertyChanged(nameof(MaterialsQuantityTotal));
        OnPropertyChanged(nameof(AverageServicePrice));
        OnPropertyChanged(nameof(AverageMaterialPrice));
        OnPropertyChanged(nameof(AdjustedServicesTotal));
        OnPropertyChanged(nameof(AdjustedMaterialsTotal));
        OnPropertyChanged(nameof(AdjustedGrandTotal));
    }

    private void BuildReceiptRows()
    {
        var serviceK = 1m + ServiceAdjustmentPercent / 100m;
        ServiceReceiptRows = new ObservableCollection<ReceiptRowVm>(
            SelectedServices.Select(s => new ReceiptRowVm(
                s.Name,
                s.Quantity,
                s.PricePerUnit,
                s.LineTotal,
                s.LineTotal * serviceK,
                ServiceAdjustmentPercent)));

        var materialK = 1m + MaterialAdjustmentPercent / 100m;
        MaterialReceiptRows = new ObservableCollection<ReceiptRowVm>(
            MaterialLines.Select(m => new ReceiptRowVm(
                m.MaterialName,
                m.Quantity,
                m.PricePerUnit,
                m.LineTotal,
                m.LineTotal * materialK,
                MaterialAdjustmentPercent)));
    }

    public void SetCreateForTask(LocalTask task, Action goBack, Func<Task>? onSavedAsync = null)
    {
        Reset();
        _task = task;
        _editStage = null;
        _goBack = goBack;
        _onSavedAsync = onSavedAsync;
        _peekProjectId = task.ProjectId;
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
        _peekProjectId = projectId;
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
        _peekProjectId = null;
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
        _peekProjectId = task.ProjectId;
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
        _peekProjectId = null;
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
        EquipmentLines.Clear();
        ErrorMessage = null;
        SelectedProjectId = null;
        SelectedTaskId = null;
        MaterialSearchText = "";
        MaterialCategoryFilter = "Все категории";
        EquipmentSearchText = "";
        EquipmentCategoryFilter = "Все категории";
        WorkerSearchText = "";
        WorkerSpecialtyFilter = "Все специальности";
        WorkerSpecialtyOptions = new ObservableCollection<string>(["Все специальности"]);
        ServiceAdjustmentPercent = 0;
        MaterialAdjustmentPercent = 0;
        ProjectRows = [];
        TaskRows = [];
        ShowProjectPickerList = true;
        NotifyWorkerCounters();
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
        await LoadEquipmentCatalogAsync();
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
        var matIds = mats.Select(m => m.MaterialId).Distinct().ToList();
        var stocks = await db.Materials
            .Where(x => matIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Quantity })
            .ToDictionaryAsync(x => x.Id, x => Math.Max(0m, x.Quantity));
        foreach (var line in MaterialLines)
        {
            if (stocks.TryGetValue(line.MaterialId, out var stock))
                line.StockAvailable = stock + line.Quantity;
        }

        var stageEquipments = await db.StageEquipments
            .Where(x => x.StageId == stageId)
            .OrderBy(x => x.EquipmentName)
            .ToListAsync();
        foreach (var se in stageEquipments)
        {
            var line = new StageEquipmentLineVm
            {
                EquipmentId = se.EquipmentId,
                EquipmentName = se.EquipmentName,
                InventoryNumber = se.InventoryNumber,
                Quantity = 1
            };
            EquipmentLines.Add(line);
        }
        ApplyServiceFilters();
        ApplyMaterialFilters();
        ApplyEquipmentFilters();
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
    partial void OnMaterialSearchTextChanged(string value) => ApplyMaterialFilters();
    partial void OnMaterialCategoryFilterChanged(string value) => ApplyMaterialFilters();
    partial void OnEquipmentSearchTextChanged(string value) => ApplyEquipmentFilters();
    partial void OnEquipmentCategoryFilterChanged(string value) => ApplyEquipmentFilters();
    partial void OnWorkerSearchTextChanged(string value) => ApplyWorkerFilters();
    partial void OnWorkerSpecialtyFilterChanged(string value) => ApplyWorkerFilters();
    partial void OnServiceAdjustmentPercentChanged(decimal value)
    {
        if (value > 999m)
        {
            ServiceAdjustmentPercent = 999m;
            return;
        }
        if (value < -999m)
        {
            ServiceAdjustmentPercent = -999m;
            return;
        }
        RecalculateTotals();
    }
    partial void OnMaterialAdjustmentPercentChanged(decimal value)
    {
        if (value > 999m)
        {
            MaterialAdjustmentPercent = 999m;
            return;
        }
        if (value < -999m)
        {
            MaterialAdjustmentPercent = -999m;
            return;
        }
        RecalculateTotals();
    }
    partial void OnErrorMessageChanged(string? value)
    {
        _errorMessageCts?.Cancel();
        _errorMessageCts?.Dispose();
        _errorMessageCts = null;
        if (string.IsNullOrWhiteSpace(value))
            return;
        var cts = new CancellationTokenSource();
        _errorMessageCts = cts;
        _ = ClearErrorDelayedAsync(cts.Token);
    }

    partial void OnSelectedProjectIdChanged(Guid? value)
    {
        if (value is { } pid)
        {
            RefreshPickerSelection(ProjectRows, pid);
            _peekProjectId = pid;
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
        var selectedServiceIds = SelectedServices.Select(s => s.TemplateId).ToHashSet();
        IEnumerable<LocalServiceTemplate> q = _allServiceTemplates;
        q = q.Where(t => !selectedServiceIds.Contains(t.Id));
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
        _allMaterialTemplates = mats;
        var materialCats = mats
            .Select(m => m.CategoryName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .Cast<string>()
            .ToList();
        MaterialCategoryOptions = new ObservableCollection<string>(["Все категории", .. materialCats]);
        MaterialCatalog = new ObservableCollection<LocalMaterial>(mats);
        ApplyMaterialFilters();
    }

    private void ApplyMaterialFilters()
    {
        var search = MaterialSearchText.Trim();
        var selectedMaterialIds = MaterialLines.Select(m => m.MaterialId).Where(id => id != Guid.Empty).ToHashSet();
        IEnumerable<LocalMaterial> q = _allMaterialTemplates;
        q = q.Where(m => !selectedMaterialIds.Contains(m.Id));
        if (!string.IsNullOrWhiteSpace(MaterialCategoryFilter) && MaterialCategoryFilter != "Все категории")
            q = q.Where(m => string.Equals(m.CategoryName, MaterialCategoryFilter, StringComparison.OrdinalIgnoreCase));
        if (search.Length > 0)
            q = q.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || (m.InventoryNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (m.CategoryName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        MaterialCatalogFiltered = new ObservableCollection<LocalMaterial>(q.ToList());
    }

    private async Task LoadEquipmentCatalogAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        _allEquipmentTemplates = await db.Equipments
            .Where(e => !e.IsWrittenOff && e.Status == "Available")
            .OrderBy(e => e.Name)
            .ToListAsync();
        var equipmentCats = _allEquipmentTemplates
            .Select(e => e.CategoryName)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .Cast<string>()
            .ToList();
        EquipmentCategoryOptions = new ObservableCollection<string>(["Все категории", .. equipmentCats]);
        ApplyEquipmentFilters();
    }

    private void ApplyEquipmentFilters()
    {
        var search = EquipmentSearchText.Trim();
        var selectedEquipmentIds = EquipmentLines.Select(x => x.EquipmentId).ToHashSet();
        IEnumerable<LocalEquipment> q = _allEquipmentTemplates;
        q = q.Where(e => !selectedEquipmentIds.Contains(e.Id));
        if (!string.IsNullOrWhiteSpace(EquipmentCategoryFilter) && EquipmentCategoryFilter != "Все категории")
            q = q.Where(e => string.Equals(e.CategoryName, EquipmentCategoryFilter, StringComparison.OrdinalIgnoreCase));
        if (search.Length > 0)
            q = q.Where(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || (e.InventoryNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (e.CategoryName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        EquipmentCatalogFiltered = new ObservableCollection<LocalEquipment>(q.ToList());
    }

    private async Task ClearErrorDelayedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            if (!token.IsCancellationRequested)
                ErrorMessage = null;
        }
        catch (TaskCanceledException) { }
    }

    private static string GetWorkerPrimarySpecialty(AssigneePickerItem item)
    {
        var subtitle = item.RoleSubtitle?.Trim();
        if (string.IsNullOrWhiteSpace(subtitle))
            return "Без специальности";
        var cut = subtitle.IndexOf('·');
        if (cut >= 0)
            subtitle = subtitle[..cut].Trim();
        return string.IsNullOrWhiteSpace(subtitle) ? "Без специальности" : subtitle;
    }

    private void RebuildWorkerSpecialtyOptions()
    {
        var options = _workerAssigneeItems
            .Select(GetWorkerPrimarySpecialty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        WorkerSpecialtyOptions = new ObservableCollection<string>(["Все специальности", .. options]);
        if (!WorkerSpecialtyOptions.Contains(WorkerSpecialtyFilter))
            WorkerSpecialtyFilter = "Все специальности";
    }

    private void ApplyWorkerFilters()
    {
        var search = WorkerSearchText.Trim();
        IEnumerable<AssigneePickerItem> q = _workerAssigneeItems;
        if (!string.IsNullOrWhiteSpace(WorkerSpecialtyFilter) && WorkerSpecialtyFilter != "Все специальности")
            q = q.Where(i => string.Equals(GetWorkerPrimarySpecialty(i), WorkerSpecialtyFilter, StringComparison.OrdinalIgnoreCase));
        if (search.Length > 0)
            q = q.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || (i.RoleSubtitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        WorkerPickerItems = new ObservableCollection<AssigneePickerItem>(q);
        NotifyWorkerCounters();
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
            RebuildWorkerSpecialtyOptions();
            ApplyWorkerFilters();
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
        NotifyWorkerCounters();
    }

    [RelayCommand]
    private void AddServiceTemplate(LocalServiceTemplate? tpl)
    {
        if (tpl is null) return;
        if (SelectedServices.Any(x => x.TemplateId == tpl.Id))
        {
            ErrorMessage = "Эта услуга уже добавлена в этап.";
            return;
        }
        var line = new StageServiceLineVm(tpl.Id, tpl.Name, tpl.Unit, tpl.BasePrice)
        {
            Quantity = 1
        };
        SelectedServices.Add(line);
        ErrorMessage = null;
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

    [RelayCommand]
    private void AddMaterialTemplate(LocalMaterial? material)
    {
        if (material is null) return;
        if (material.Quantity <= 0m)
        {
            ErrorMessage = "Этого материала нет на складе.";
            return;
        }
        if (MaterialLines.Any(x => x.MaterialId == material.Id))
        {
            ErrorMessage = "Этот материал уже добавлен в этап.";
            return;
        }
        var line = new StageMaterialLineVm();
        line.ApplyFrom(material);
        line.Quantity = 1;
        MaterialLines.Add(line);
        RecalculateTotals();
        ErrorMessage = null;
    }

    public void AdjustServiceQuantity(StageServiceLineVm line, decimal delta)
    {
        var n = Math.Round(line.Quantity + delta, 2, MidpointRounding.AwayFromZero);
        line.Quantity = Math.Max(1m, n);
    }

    public void AdjustMaterialQuantity(StageMaterialLineVm line, decimal delta)
    {
        var n = Math.Round(line.Quantity + delta, 2, MidpointRounding.AwayFromZero);
        var maxAllowed = line.StockAvailable > 0m ? line.StockAvailable : decimal.MaxValue;
        line.Quantity = Math.Min(Math.Max(1m, n), maxAllowed);
    }

    public void AdjustEquipmentQuantity(StageEquipmentLineVm line, decimal delta)
    {
        line.Quantity = 1m;
    }

    [RelayCommand]
    private void AddServiceMarkup()
    {
        ServiceAdjustmentPercent += 5;
        RecalculateTotals();
    }

    [RelayCommand]
    private void AddServiceDiscount()
    {
        ServiceAdjustmentPercent -= 5;
        RecalculateTotals();
    }

    [RelayCommand]
    private void AddMaterialMarkup()
    {
        MaterialAdjustmentPercent += 5;
        RecalculateTotals();
    }

    [RelayCommand]
    private void AddMaterialDiscount()
    {
        MaterialAdjustmentPercent -= 5;
        RecalculateTotals();
    }

    [RelayCommand]
    private void ResetServiceAdjustment()
    {
        ServiceAdjustmentPercent = 0;
        RecalculateTotals();
    }

    [RelayCommand]
    private void ResetMaterialAdjustment()
    {
        MaterialAdjustmentPercent = 0;
        RecalculateTotals();
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
    private void AddEquipmentTemplate(LocalEquipment? equipment)
    {
        if (equipment is null) return;
        if (EquipmentLines.Any(x => x.EquipmentId == equipment.Id))
        {
            ErrorMessage = "Это оборудование уже добавлено в этап.";
            return;
        }
        var line = new StageEquipmentLineVm { Quantity = 1 };
        line.ApplyFrom(equipment);
        EquipmentLines.Add(line);
        ApplyEquipmentFilters();
        ErrorMessage = null;
    }

    [RelayCommand]
    private void RemoveEquipmentLine(StageEquipmentLineVm? line)
    {
        if (line is null) return;
        EquipmentLines.Remove(line);
        ApplyEquipmentFilters();
    }

    private void NotifyWorkerCounters()
    {
        OnPropertyChanged(nameof(SelectedWorkersCount));
        OnPropertyChanged(nameof(AvailableWorkersCount));
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

        if (IsWorker() && _editStage is null)
        {
            ErrorMessage = "Работники не могут создавать этапы.";
            return;
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
            if (ml.Quantity < 1m)
            {
                ErrorMessage = "Количество материалов не может быть меньше 1.";
                return;
            }
            if (ml.StockAvailable > 0m && ml.Quantity > ml.StockAvailable)
            {
                ErrorMessage = $"Материала \"{ml.MaterialName}\" недостаточно на складе. Доступно: {ml.StockAvailable:N2}.";
                return;
            }
        }

        foreach (var sl in SelectedServices)
        {
            if (sl.Quantity < 1m)
            {
                ErrorMessage = "Количество услуг не может быть меньше 1.";
                return;
            }
        }

        var serviceItems = SelectedServices
            .Select(s => new StageServiceItemRequest(s.TemplateId, s.Quantity, s.PricePerUnit))
            .ToList();
        var equipmentEntities = EquipmentLines
            .Select(e => new LocalStageEquipment
            {
                Id = Guid.NewGuid(),
                StageId = Guid.Empty,
                EquipmentId = e.EquipmentId,
                EquipmentName = e.EquipmentName,
                InventoryNumber = e.InventoryNumber,
                IsSynced = false,
                LastModifiedLocally = DateTime.UtcNow
            })
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
            await taskVm.ReplaceStageEquipmentsAsync(stageId, equipmentEntities);

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

public sealed class ReceiptRowVm
{
    public string Name { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal BaseTotal { get; }
    public decimal AdjustedTotal { get; }
    public decimal AdjustmentPercent { get; }
    public string AdjustmentLabel =>
        AdjustmentPercent == 0 ? "0%" :
        AdjustmentPercent > 0 ? $"+{AdjustmentPercent:N0}%" : $"{AdjustmentPercent:N0}%";

    public ReceiptRowVm(
        string name,
        decimal quantity,
        decimal unitPrice,
        decimal baseTotal,
        decimal adjustedTotal,
        decimal adjustmentPercent)
    {
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        BaseTotal = baseTotal;
        AdjustedTotal = adjustedTotal;
        AdjustmentPercent = adjustmentPercent;
    }
}
