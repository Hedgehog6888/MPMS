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

public partial class StageDetailViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IServiceProvider _sp;

    private LocalTask? _task;
    private LocalTaskStage? _editStage;
    private Action? _goBack;
    public LocalTaskStage? EditStage => _editStage;
    public LocalTask? EditTask => _task;
    private Func<Task>? _onSavedAsync;
    private readonly HashSet<Guid> _selectedAssigneeIds = [];
    private List<AssigneePickerItem> _workerAssigneeItems = [];
    private List<AssigneePickerItem> _allAssigneeItems = [];
    private CancellationTokenSource? _errorMessageCts;
    private Guid? _peekProjectId;
    private bool _isLoaded;
    private bool _catalogDirty;
    private bool _suppressCatalogDirty;

    [ObservableProperty] private string _pageTitle = "Добавить этап";
    [ObservableProperty] private string _saveButtonText = "Добавить этап";
    [ObservableProperty] private string _contextSubtitle = "";
    [ObservableProperty] private string _projectNameReadOnly = "";
    [ObservableProperty] private string _stageName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private StageStatus _stageStatus = StageStatus.Planned;
    [ObservableProperty] private bool _isProjectClosed;
    public FilesControlViewModel FilesControlVM { get; }
    [ObservableProperty] private bool _isOverdue;
    [ObservableProperty] private bool _isViewMode;
    [ObservableProperty] private bool _showStatusManagement;
    [ObservableProperty] private bool _canStartStage;
    [ObservableProperty] private bool _canCompleteStage;
    [ObservableProperty] private bool _canMarkStageForDeletion;
    [ObservableProperty] private bool _isStageMarkedForDeletion;
    [ObservableProperty] private string _activeTab = "Main";
    [ObservableProperty] private bool _isCatalogEditMode;

    [ObservableProperty] private bool _showProjectTaskPickers;
    [ObservableProperty] private bool _showProjectNameRow;
    [ObservableProperty] private bool _showProjectPickerList = true;

    [ObservableProperty] private ObservableCollection<PickerRowVm> _projectRows = [];
    [ObservableProperty] private ObservableCollection<PickerRowVm> _taskRows = [];
    [ObservableProperty] private Guid? _selectedProjectId;
    [ObservableProperty] private Guid? _selectedTaskId;

    [ObservableProperty] private string _serviceSearchText = "";
    [ObservableProperty] private string _serviceCategoryFilter = "Все категории";
    [ObservableProperty] private ObservableCollection<string> _serviceCategoryOptions = [];
    [ObservableProperty] private ObservableCollection<LocalWorkTypeTemplate> _serviceCatalogFiltered = [];
    private List<LocalWorkTypeTemplate> _allServiceTemplates = [];

    [ObservableProperty] private string _materialSearchText = "";
    [ObservableProperty] private string _materialCategoryFilter = "Все категории";
    [ObservableProperty] private ObservableCollection<string> _materialCategoryOptions = [];
    [ObservableProperty] private string _equipmentSearchText = "";
    [ObservableProperty] private string _equipmentCategoryFilter = "Все категории";
    [ObservableProperty] private ObservableCollection<string> _equipmentCategoryOptions = [];
    [ObservableProperty] private ObservableCollection<StageWorkTypeLineVm> _selectedServices = [];
    [ObservableProperty] private ObservableCollection<StageWorkTypeLineVm> _selectedServicesFiltered = [];
    [ObservableProperty] private ObservableCollection<StageMaterialLineVm> _materialLines = [];
    [ObservableProperty] private ObservableCollection<StageMaterialLineVm> _materialLinesFiltered = [];
    [ObservableProperty] private ObservableCollection<StageEquipmentLineVm> _equipmentLines = [];
    [ObservableProperty] private ObservableCollection<StageEquipmentLineVm> _equipmentLinesFiltered = [];
    [ObservableProperty] private ObservableCollection<LocalMaterial> _materialCatalog = [];
    [ObservableProperty] private ObservableCollection<LocalMaterial> _materialCatalogFiltered = [];
    private List<LocalMaterial> _allMaterialTemplates = [];
    [ObservableProperty] private ObservableCollection<LocalEquipment> _equipmentCatalogFiltered = [];
    private List<LocalEquipment> _allEquipmentTemplates = [];


    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private List<AssigneePickerItem> _foremanMembers = [];
    [ObservableProperty] private List<AssigneePickerItem> _workerMembers = [];

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
    public decimal ProgressMaximum => Math.Max(1m, SummaryServicesTotal + SummaryMaterialsTotal);
    public Guid? PeekProjectId => _peekProjectId;

    public bool IsStagePlanned => StageStatus == StageStatus.Planned;

    public bool IsStageInProgress => StageStatus == StageStatus.InProgress;

    public bool CanEditStageDetails => !IsStageMarkedForDeletion && IsStagePlanned;

    public string? EditStageDisabledTooltip
    {
        get
        {
            if (IsStageMarkedForDeletion)
                return "Сначала снимите пометку удаления";
            if (!IsStagePlanned)
                return "Редактирование доступно только для запланированного этапа";
            return null;
        }
    }

    public bool IsStageCatalogEditable =>
        !IsStageMarkedForDeletion
        && (IsStagePlanned
            || StageStatus == StageStatus.Completed
            || (IsStageInProgress && IsCatalogEditMode));

    public bool IsStageCatalogReadOnly => !IsStageCatalogEditable;

    public bool CanUploadStageFiles => !IsStageMarkedForDeletion;

    public bool CanEditStageSummary => !IsStageMarkedForDeletion;

    public bool ShowSummaryTab => !IsWorker();

    public bool ShowStageUploadButton => ActiveTab == "Files" && CanUploadStageFiles;

    public bool CanEditServicesCatalog => IsManagerOrForeman();

    public bool CanEditMaterialsCatalog => true;

    public bool CanEditEquipmentCatalog => true;

    public bool ShowCatalogEditButton =>
        IsStageInProgress
        && !IsStageMarkedForDeletion
        && ActiveTab switch
        {
            "Services" => CanEditServicesCatalog,
            "Materials" => CanEditMaterialsCatalog,
            "Equipment" => CanEditEquipmentCatalog,
            _ => false
        };

    public string CatalogEditButtonText => IsCatalogEditMode ? "Готово" : "Редактировать";

    public StageDetailViewModel(
        IDbContextFactory<LocalDbContext> dbFactory,
        IAuthService auth,
        IServiceProvider sp)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _sp = sp;
        FilesControlVM = sp.GetRequiredService<FilesControlViewModel>();

        SelectedServices.CollectionChanged += OnTotalsCollectionChanged;
        MaterialLines.CollectionChanged += OnTotalsCollectionChanged;
        EquipmentLines.CollectionChanged += OnEquipmentCollectionChanged;
    }

    private void OnEquipmentCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MarkCatalogDirty();
        ApplyEquipmentFilters();
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
        MarkCatalogDirty();
        if (ReferenceEquals(sender, SelectedServices))
            ApplyServiceFilters();
        else if (ReferenceEquals(sender, MaterialLines))
            ApplyMaterialFilters();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageWorkTypeLineVm.LineTotal)
            or nameof(StageMaterialLineVm.LineTotal)
            or nameof(StageWorkTypeLineVm.PricePerUnit)
            or nameof(StageMaterialLineVm.PricePerUnit)
            or nameof(StageWorkTypeLineVm.LineAdjustmentPercent)
            or nameof(StageMaterialLineVm.LineAdjustmentPercent))
        {
            RecalculateTotals();
            MarkCatalogDirty();
        }
    }

    private void MarkCatalogDirty()
    {
        if (!_suppressCatalogDirty)
            _catalogDirty = true;
    }

    private void MarkCatalogClean() => _catalogDirty = false;

    private static List<StageWorkTypeItemRequest> BuildServiceItems(
        IEnumerable<StageWorkTypeLineVm> lines) =>
        lines.Select(s => new StageWorkTypeItemRequest(
            s.TemplateId,
            s.Quantity,
            s.PricePerUnit,
            s.Name,
            s.Unit,
            s.BasePricePerUnit,
            s.LineAdjustmentPercent)).ToList();

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
        OnPropertyChanged(nameof(ProgressMaximum));
    }

    private void BuildReceiptRows()
    {
        var serviceK = 1m + ServiceAdjustmentPercent / 100m;
        ServiceReceiptRows = new ObservableCollection<ReceiptRowVm>(
            SelectedServices.Select(s => ReceiptRowVm.ForService(
                s,
                ServiceAdjustmentPercent,
                serviceK)));

        var materialK = 1m + MaterialAdjustmentPercent / 100m;
        MaterialReceiptRows = new ObservableCollection<ReceiptRowVm>(
            MaterialLines.Select(m => ReceiptRowVm.ForMaterial(
                m,
                MaterialAdjustmentPercent,
                materialK)));
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
        StageStatus = stage.Status;
        IsStageMarkedForDeletion = stage.IsMarkedForDeletion;
        IsOverdue = stage.IsOverdue;
        ShowProjectTaskPickers = false;
        ShowProjectNameRow = true;
        ShowProjectPickerList = true;
        IsViewMode = true;
        ShowStatusManagement = !IsWorker();
        CanStartStage = stage.Status == StageStatus.Planned && !stage.IsMarkedForDeletion;
        CanCompleteStage = stage.Status == StageStatus.InProgress && !stage.IsMarkedForDeletion;
        CanMarkStageForDeletion = !IsWorker();
        _ = LoadAssigneesForDisplayAsync(task.Id, stage.Id);
        _ = LoadExistingServicesAndMaterialsAsync(stage.Id);
        FilesControlVM.Initialize(task.ProjectId, task.Id, stage.Id);
    }

    public void SetViewMode(LocalTaskStage stage, LocalTask task, Action goBack)
    {
        Reset();
        _editStage = stage;
        _task = task;
        _goBack = goBack;
        _peekProjectId = task.ProjectId;
        PageTitle = stage.Name;
        SaveButtonText = "Редактировать этап";
        ContextSubtitle = $"Задача: {task.Name}";
        ProjectNameReadOnly = task.ProjectName ?? "—";
        StageName = stage.Name;
        Description = stage.Description ?? "";
        DueDate = stage.DueDate?.ToDateTime(TimeOnly.MinValue);
        StageStatus = stage.Status;
        IsStageMarkedForDeletion = stage.IsMarkedForDeletion;
        IsOverdue = stage.IsOverdue;
        ShowProjectTaskPickers = false;
        ShowProjectNameRow = true;
        ShowProjectPickerList = true;
        IsViewMode = true;
        ShowStatusManagement = !IsWorker();
        CanStartStage = stage.Status == StageStatus.Planned && !stage.IsMarkedForDeletion;
        CanCompleteStage = stage.Status == StageStatus.InProgress && !stage.IsMarkedForDeletion;
        CanMarkStageForDeletion = !IsWorker();
        _ = LoadAssigneesForDisplayAsync(task.Id, stage.Id);
        _ = LoadExistingServicesAndMaterialsAsync(stage.Id);
        FilesControlVM.Initialize(task.ProjectId, task.Id, stage.Id);
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
        _allAssigneeItems = [];
        StageName = "";
        Description = "";
        DueDate = null;
        ActiveTab = "Main";
        IsCatalogEditMode = false;
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
        ServiceAdjustmentPercent = 0;
        MaterialAdjustmentPercent = 0;
        ProjectRows = [];
        TaskRows = [];
        ShowProjectPickerList = true;
        IsViewMode = false;
        ShowStatusManagement = false;
        CanStartStage = false;
        CanCompleteStage = false;
        CanMarkStageForDeletion = false;
        IsStageMarkedForDeletion = false;
        IsOverdue = false;
        StageStatus = StageStatus.Planned;
        ForemanMembers = [];
        WorkerMembers = [];
        _isLoaded = false;
        MarkCatalogClean();
    }

    private async Task LoadProjectNameAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var p = await db.Projects.FindAsync(projectId);
        ProjectNameReadOnly = p?.Name ?? "—";
    }


    private bool IsWorker() =>
        string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

    private bool IsManagerOrForeman() =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager" or "Foreman";

    partial void OnActiveTabChanged(string value)
    {
        if (value == "Summary" && !ShowSummaryTab)
        {
            ActiveTab = "Main";
            return;
        }

        if (IsCatalogEditMode)
            _ = ExitCatalogEditModeAsync(saveChanges: true);
        else
            RefreshCatalogModeProperties();
        OnPropertyChanged(nameof(ShowStageUploadButton));
    }

    partial void OnStageStatusChanged(StageStatus value)
    {
        IsCatalogEditMode = false;
        RefreshCatalogModeProperties();
    }

    partial void OnIsCatalogEditModeChanged(bool value) => RefreshCatalogModeProperties();

    partial void OnIsStageMarkedForDeletionChanged(bool value)
    {
        if (value)
            IsCatalogEditMode = false;
        RefreshCatalogModeProperties();
    }

    private void RefreshCatalogModeProperties()
    {
        OnPropertyChanged(nameof(IsStagePlanned));
        OnPropertyChanged(nameof(IsStageInProgress));
        OnPropertyChanged(nameof(CanEditStageDetails));
        OnPropertyChanged(nameof(EditStageDisabledTooltip));
        OnPropertyChanged(nameof(IsStageCatalogEditable));
        OnPropertyChanged(nameof(IsStageCatalogReadOnly));
        OnPropertyChanged(nameof(CanUploadStageFiles));
        OnPropertyChanged(nameof(CanEditStageSummary));
        OnPropertyChanged(nameof(ShowStageUploadButton));
        OnPropertyChanged(nameof(CanEditServicesCatalog));
        OnPropertyChanged(nameof(CanEditMaterialsCatalog));
        OnPropertyChanged(nameof(CanEditEquipmentCatalog));
        OnPropertyChanged(nameof(ShowCatalogEditButton));
        OnPropertyChanged(nameof(CatalogEditButtonText));
    }

    [RelayCommand]
    private async Task ToggleCatalogEditModeAsync()
    {
        if (IsStageMarkedForDeletion) return;
        if (IsCatalogEditMode)
            await ExitCatalogEditModeAsync(saveChanges: true);
        else
            IsCatalogEditMode = true;
    }

    private async Task ExitCatalogEditModeAsync(bool saveChanges)
    {
        if (!IsCatalogEditMode) return;

        if (saveChanges)
        {
            var saved = await SaveStageCatalogAsync();
            if (!saved) return;
        }

        IsCatalogEditMode = false;
    }

    public async Task<bool> SaveStageCatalogAsync()
    {
        if (_editStage is null || _task is null) return false;
        if (IsStageMarkedForDeletion) return false;

        ErrorMessage = null;

        foreach (var ml in MaterialLines)
        {
            if (ml.MaterialId == Guid.Empty)
            {
                ErrorMessage = "Укажите материал во всех строках или удалите пустые";
                return false;
            }
            if (ml.Quantity < 1m)
            {
                ErrorMessage = "Количество материалов не может быть меньше 1";
                return false;
            }
            if (ml.StockAvailable > 0m && ml.Quantity > ml.StockAvailable)
            {
                ErrorMessage = $"Материала \"{ml.MaterialName}\" недостаточно на складе. Доступно: {ml.StockAvailable:N2}";
                return false;
            }
        }

        foreach (var sl in SelectedServices)
        {
            if (sl.Quantity < 1m)
            {
                ErrorMessage = "Количество услуг не может быть меньше 1";
                return false;
            }
        }

        var serviceItems = BuildServiceItems(SelectedServices);
        var equipmentEntities = EquipmentLines
            .Select(e => new LocalStageEquipment
            {
                Id = Guid.NewGuid(),
                StageId = _editStage.Id,
                EquipmentId = e.EquipmentId,
                EquipmentName = e.EquipmentName,
                InventoryNumber = e.InventoryNumber,
                IsSynced = false,
                LastModifiedLocally = DateTime.UtcNow
            })
            .ToList();
        var matEntities = MaterialLines.Select(m => new LocalStageMaterial
        {
            Id = Guid.NewGuid(),
            StageId = _editStage.Id,
            MaterialId = m.MaterialId,
            MaterialName = m.MaterialName,
            Unit = m.Unit,
            Quantity = m.Quantity,
            PricePerUnit = m.PricePerUnit,
            BasePricePerUnit = m.BasePricePerUnit,
            LineAdjustmentPercent = m.LineAdjustmentPercent,
            IsSynced = false,
            LastModifiedLocally = DateTime.UtcNow
        }).ToList();

        IsBusy = true;
        try
        {
            var taskVm = _sp.GetRequiredService<TaskDetailViewModel>();
            taskVm.SetTask(_task);

            await taskVm.ReplaceStageWorkTypesAsync(_editStage.Id, serviceItems);
            await taskVm.ReplaceStageMaterialsAsync(_editStage.Id, matEntities);
            await taskVm.ReplaceStageEquipmentsAsync(_editStage.Id, equipmentEntities);
            await taskVm.SaveStageSummaryPricingAsync(_editStage.Id, ServiceAdjustmentPercent, MaterialAdjustmentPercent);

            SelectedServices.Clear();
            MaterialLines.Clear();
            EquipmentLines.Clear();
            await LoadExistingServicesAndMaterialsAsync(_editStage.Id);
            RecalculateTotals();
            MarkCatalogClean();
            await LoadMaterialCatalogAsync();

            if (_onSavedAsync is not null)
                await _onSavedAsync();

            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadAsync()
    {
        if (_isLoaded) return;
        await LoadServiceCatalogAsync();
        await LoadMaterialCatalogAsync();
        await LoadEquipmentCatalogAsync();
        RecalculateTotals();
        _isLoaded = true;
    }
    public async Task ReloadAllAsync()
    {
        if (_editStage is null || _task is null) return;
        _isLoaded = false;
        await LoadAssigneesForDisplayAsync(_task.Id, _editStage.Id);
        await LoadExistingServicesAndMaterialsAsync(_editStage.Id);
        await LoadAsync();
    }
    private async Task LoadExistingServicesAndMaterialsAsync(Guid stageId)
    {
        _suppressCatalogDirty = true;
        try
        {
            SelectedServices.Clear();
            MaterialLines.Clear();
            EquipmentLines.Clear();

            await using var db = await _dbFactory.CreateDbContextAsync();
            var stage = await db.TaskStages.FindAsync(stageId);
            if (stage is not null)
            {
                ServiceAdjustmentPercent = stage.ServicesAdjustmentPercent;
                MaterialAdjustmentPercent = stage.MaterialsAdjustmentPercent;
            }

            var svcs = await db.StageWorkTypes
            .Where(s => s.StageId == stageId)
            .OrderBy(s => s.WorkTypeName)
            .ToListAsync();
        foreach (var s in svcs)
        {
            var basePrice = s.BasePricePerUnit > 0m ? s.BasePricePerUnit : s.PricePerUnit;
            var line = new StageWorkTypeLineVm(s.WorkTypeTemplateId, s.WorkTypeName, s.Unit, basePrice)
            {
                Quantity = s.Quantity,
                PricePerUnit = s.PricePerUnit,
                LineAdjustmentPercent = s.LineAdjustmentPercent
            };
            SelectedServices.Add(line);
        }
        var mats = await db.StageMaterials
            .Where(m => m.StageId == stageId)
            .OrderBy(m => m.MaterialName)
            .ToListAsync();
        foreach (var m in mats)
        {
            var basePrice = m.BasePricePerUnit > 0m ? m.BasePricePerUnit : m.PricePerUnit;
            var line = new StageMaterialLineVm(m.MaterialId, m.MaterialName, m.Unit, basePrice)
            {
                Quantity = m.Quantity,
                PricePerUnit = m.PricePerUnit,
                LineAdjustmentPercent = m.LineAdjustmentPercent
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
        RecalculateTotals();
        }
        finally
        {
            _suppressCatalogDirty = false;
            MarkCatalogClean();
        }
    }

    private async Task LoadServiceCatalogAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        _allServiceTemplates = await db.WorkTypeTemplates
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
        }
    }

    private void ApplyServiceFilters()
    {
        var selectedServiceIds = SelectedServices.Select(s => s.TemplateId).ToHashSet();
        IEnumerable<LocalWorkTypeTemplate> q = _allServiceTemplates;
        q = q.Where(t => !selectedServiceIds.Contains(t.Id));
        if (!string.IsNullOrWhiteSpace(ServiceCategoryFilter) && ServiceCategoryFilter != "Все категории")
            q = q.Where(t => t.CategoryName == ServiceCategoryFilter);
        var s = ServiceSearchText.Trim();
        if (s.Length > 0)
            q = q.Where(t => t.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                             || (t.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (t.Article?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        ServiceCatalogFiltered = new ObservableCollection<LocalWorkTypeTemplate>(q.ToList());
        ApplySelectedServicesFilter();
    }

    private void ApplySelectedServicesFilter()
    {
        var search = ServiceSearchText.Trim();
        IEnumerable<StageWorkTypeLineVm> q = SelectedServices;
        if (search.Length > 0)
            q = q.Where(s => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        SelectedServicesFiltered = new ObservableCollection<StageWorkTypeLineVm>(q.ToList());
    }

    private async Task LoadMaterialCatalogAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var mats = await db.Materials
            .Where(m => !m.IsWrittenOff && m.Quantity > 0m)
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
        q = q.Where(m => m.Quantity > 0m);
        q = q.Where(m => !selectedMaterialIds.Contains(m.Id));
        if (!string.IsNullOrWhiteSpace(MaterialCategoryFilter) && MaterialCategoryFilter != "Все категории")
            q = q.Where(m => string.Equals(m.CategoryName, MaterialCategoryFilter, StringComparison.OrdinalIgnoreCase));
        if (search.Length > 0)
            q = q.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || (m.InventoryNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (m.CategoryName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        MaterialCatalogFiltered = new ObservableCollection<LocalMaterial>(q.ToList());
        ApplyMaterialLinesFilter();
    }

    private void ApplyMaterialLinesFilter()
    {
        var search = MaterialSearchText.Trim();
        IEnumerable<StageMaterialLineVm> q = MaterialLines;
        if (search.Length > 0)
            q = q.Where(m => m.MaterialName.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || (m.Unit?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        MaterialLinesFiltered = new ObservableCollection<StageMaterialLineVm>(q.ToList());
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
        ApplyEquipmentLinesFilter();
    }

    private void ApplyEquipmentLinesFilter()
    {
        var search = EquipmentSearchText.Trim();
        IEnumerable<StageEquipmentLineVm> q = EquipmentLines;
        if (search.Length > 0)
            q = q.Where(e => e.EquipmentName.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || (e.InventoryNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        EquipmentLinesFiltered = new ObservableCollection<StageEquipmentLineVm>(q.ToList());
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


    private async Task LoadProjectsForPickerAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var projects = await db.Projects
            .Where(p => !p.IsArchived && !p.IsClosed && !p.IsMarkedForDeletion)
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

    private async Task LoadAssigneesForDisplayAsync(Guid taskId, Guid? stageId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var blockedUserIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();

        List<Guid> assigneeIds;
        if (stageId.HasValue)
        {
            assigneeIds = await db.StageAssignees
                .Where(sa => sa.StageId == stageId.Value && !blockedUserIds.Contains(sa.UserId))
                .Select(sa => sa.UserId)
                .Distinct()
                .ToListAsync();

            if (assigneeIds.Count == 0)
            {
                var stage = await db.TaskStages.FindAsync(stageId.Value);
                if (stage?.AssignedUserId.HasValue == true && !blockedUserIds.Contains(stage.AssignedUserId.Value))
                    assigneeIds.Add(stage.AssignedUserId.Value);
            }
        }
        else
        {
            assigneeIds = await db.TaskAssignees
                .Where(ta => ta.TaskId == taskId && !blockedUserIds.Contains(ta.UserId))
                .Select(ta => ta.UserId)
                .Distinct()
                .ToListAsync();

            if (assigneeIds.Count == 0)
            {
                var task = await db.Tasks.FindAsync(taskId);
                if (task?.AssignedUserId.HasValue == true && !blockedUserIds.Contains(task.AssignedUserId.Value))
                    assigneeIds.Add(task.AssignedUserId.Value);
            }
        }

        if (assigneeIds.Count == 0)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ForemanMembers = [];
                WorkerMembers = [];
            });
            return;
        }

        var users = await db.Users
            .Where(u => assigneeIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.AvatarData, u.AvatarPath, u.RoleName, u.SubRole, u.AdditionalSubRoles })
            .ToListAsync();

        var assigneeItems = users.Select(u => new AssigneePickerItem(
            u.Id,
            u.Name ?? "—",
            u.RoleName ?? "Worker",
            new HashSet<Guid>(),
            u.AvatarPath,
            u.AvatarData,
            u.SubRole,
            u.AdditionalSubRoles)).ToList();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ForemanMembers = assigneeItems.Where(i => i.IsForemanPicker).ToList();
            WorkerMembers = assigneeItems.Where(i => i.RoleDisplay == "Работник").ToList();
        });
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
        }
    }


    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (_editStage is not null && !IsStageMarkedForDeletion && (_catalogDirty || IsCatalogEditMode))
        {
            var saved = await SaveStageCatalogAsync();
            if (!saved) return;
            IsCatalogEditMode = false;
        }

        _goBack?.Invoke();
    }

    [RelayCommand]
    private async Task StartStageAsync()
    {
        if (_editStage is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(_editStage.Id);
        if (stage is null) return;
        stage.Status = StageStatus.InProgress;
        stage.IsSynced = false;
        await db.SaveChangesAsync();
        StageStatus = StageStatus.InProgress;
        IsCatalogEditMode = false;
        CanStartStage = false;
        CanCompleteStage = true;
        RefreshCatalogModeProperties();
    }

    [RelayCommand]
    private async Task CompleteStageAsync()
    {
        if (_editStage is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(_editStage.Id);
        if (stage is null) return;
        stage.Status = StageStatus.Completed;
        stage.IsSynced = false;
        await db.SaveChangesAsync();
        StageStatus = StageStatus.Completed;
        CanStartStage = false;
        CanCompleteStage = false;
    }

    [RelayCommand]
    private async Task MarkStageForDeletionAsync()
    {
        if (_editStage is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(_editStage.Id);
        if (stage is null) return;
        stage.IsMarkedForDeletion = !stage.IsMarkedForDeletion;
        stage.IsSynced = false;
        await db.SaveChangesAsync();
        IsStageMarkedForDeletion = stage.IsMarkedForDeletion;
        CanStartStage = !IsStageMarkedForDeletion && StageStatus == StageStatus.Planned;
        CanCompleteStage = !IsStageMarkedForDeletion && StageStatus == StageStatus.InProgress;
    }


    [RelayCommand]
    private void AddWorkTypeTemplate(LocalWorkTypeTemplate? tpl)
    {
        if (tpl is null) return;
        if (SelectedServices.Any(x => x.TemplateId == tpl.Id))
        {
            ErrorMessage = "Эта услуга уже добавлена в этап";
            return;
        }
        var line = new StageWorkTypeLineVm(tpl.Id, tpl.Name, tpl.Unit, tpl.BasePrice)
        {
            Quantity = 1
        };
        SelectedServices.Add(line);
        ErrorMessage = null;
    }

    [RelayCommand]
    private void RemoveServiceLine(StageWorkTypeLineVm? line)
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
            ErrorMessage = "Этого материала нет на складе";
            return;
        }
        if (MaterialLines.Any(x => x.MaterialId == material.Id))
        {
            ErrorMessage = "Этот материал уже добавлен в этап";
            return;
        }
        var line = new StageMaterialLineVm();
        line.ApplyFrom(material);
        line.Quantity = 1;
        MaterialLines.Add(line);
        RecalculateTotals();
        ErrorMessage = null;
    }

    public void AdjustWorkTypeQuantity(StageWorkTypeLineVm line, decimal delta)
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

    public void OpenReceiptLinePricing(ReceiptRowVm row)
    {
        if (!CanEditStageSummary) return;
        if (MainWindow.Instance is null) return;

        if (row.IsServiceLine)
        {
            var line = SelectedServices.FirstOrDefault(s => s.TemplateId == row.RowKey);
            if (line is null) return;
            var overlay = new StageLinePricingOverlay(
                line.Name,
                line.BasePricePerUnit,
                line.Quantity,
                line.PricePerUnit,
                line.LineAdjustmentPercent,
                new StageLinePricingOptions { Unit = line.Unit },
                (percent, price, quantity) => ApplyOverlayLineChangesAsync(line, null, percent, price, quantity));
            MainWindow.Instance.ShowCenteredOverlay(overlay, 520);
            return;
        }

        var materialLine = MaterialLines.FirstOrDefault(m => m.MaterialId == row.MaterialId);
        if (materialLine is null) return;
        var materialOverlay = new StageLinePricingOverlay(
            materialLine.MaterialName,
            materialLine.BasePricePerUnit,
            materialLine.Quantity,
            materialLine.PricePerUnit,
            materialLine.LineAdjustmentPercent,
            new StageLinePricingOptions
            {
                IsMaterial = true,
                Unit = materialLine.Unit,
                StockAvailable = materialLine.StockAvailable
            },
            (percent, price, quantity) => ApplyOverlayLineChangesAsync(null, materialLine, percent, price, quantity));
        MainWindow.Instance.ShowCenteredOverlay(materialOverlay, 520);
    }

    private async Task<bool> ApplyOverlayLineChangesAsync(
        StageWorkTypeLineVm? serviceLine,
        StageMaterialLineVm? materialLine,
        decimal percent,
        decimal price,
        decimal quantity)
    {
        if (serviceLine is not null)
            serviceLine.ApplyStagePricing(percent, price, quantity);
        else if (materialLine is not null)
            materialLine.ApplyStagePricing(percent, price, quantity);

        return await SaveStageCatalogAsync();
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
            ErrorMessage = "Это оборудование уже добавлено в этап";
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

    private void RebuildMemberLists()
    {
        ForemanMembers = [.. _allAssigneeItems
            .Where(i => i.IsForemanPicker)];
        WorkerMembers = [.. _workerAssigneeItems
            .Where(i => _selectedAssigneeIds.Contains(i.UserId))];
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
            ErrorMessage = "Введите название этапа";
            return;
        }

        Guid taskId;
        TaskDetailViewModel taskVm = _sp.GetRequiredService<TaskDetailViewModel>();

        if (ShowProjectTaskPickers)
        {
            if (SelectedTaskId is not Guid tid)
            {
                ErrorMessage = "Выберите задачу";
                return;
            }
            taskId = tid;
            var t = await GetTaskByIdAsync(taskId);
            if (t is null)
            {
                ErrorMessage = "Задача не найдена";
                return;
            }
            taskVm.SetTask(t);
            _task = t;
        }
        else
        {
            if (_task is null)
            {
                ErrorMessage = "Задача не выбрана";
                return;
            }
            taskVm.SetTask(_task);
            taskId = _task.Id;
        }

        if (IsWorker() && _editStage is null)
        {
            ErrorMessage = "Работники не могут создавать этапы";
            return;
        }


        Guid? primaryAssigneeId = _selectedAssigneeIds.Count > 0 ? _selectedAssigneeIds.FirstOrDefault() : null;

        DateOnly? dueDate = DueDate is { } sd ? DateOnly.FromDateTime(sd) : null;
        if (!DueDatePolicy.IsAllowedForUpdate(dueDate, _editStage?.DueDate))
        {
            ErrorMessage = DueDatePolicy.PastNotAllowedMessage;
            return;
        }

        foreach (var ml in MaterialLines)
        {
            if (ml.MaterialId == Guid.Empty)
            {
                ErrorMessage = "Укажите материал во всех строках или удалите пустые";
                return;
            }
            if (ml.Quantity < 1m)
            {
                ErrorMessage = "Количество материалов не может быть меньше 1";
                return;
            }
            if (ml.StockAvailable > 0m && ml.Quantity > ml.StockAvailable)
            {
                ErrorMessage = $"Материала \"{ml.MaterialName}\" недостаточно на складе. Доступно: {ml.StockAvailable:N2}";
                return;
            }
        }

        foreach (var sl in SelectedServices)
        {
            if (sl.Quantity < 1m)
            {
                ErrorMessage = "Количество услуг не может быть меньше 1";
                return;
            }
        }

        var serviceItems = BuildServiceItems(SelectedServices);
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
                var status = _editStage.Status;
                var req = new UpdateStageRequest(
                    StageName.Trim(),
                    string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    primaryAssigneeId,
                    status,
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
                BasePricePerUnit = m.BasePricePerUnit,
                LineAdjustmentPercent = m.LineAdjustmentPercent,
                IsSynced = false,
                LastModifiedLocally = DateTime.UtcNow
            }).ToList();
            await taskVm.ReplaceStageMaterialsAsync(stageId, matEntities);
            await taskVm.ReplaceStageEquipmentsAsync(stageId, equipmentEntities);
            await taskVm.SaveStageSummaryPricingAsync(stageId, ServiceAdjustmentPercent, MaterialAdjustmentPercent);

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
    public decimal BaseUnitPrice { get; }
    public decimal EffectiveUnitPrice { get; }
    public bool HasPriceOverride { get; }
    public decimal BaseTotal { get; }
    public decimal AdjustedTotal { get; }
    public decimal GlobalAdjustmentPercent { get; }
    public decimal LineAdjustmentPercent { get; }
    public Guid RowKey { get; }
    public Guid MaterialId { get; }
    public bool IsServiceLine { get; }
    public string AdjustmentLabel => FormatPercent(GlobalAdjustmentPercent);

    private ReceiptRowVm(
        string name,
        decimal quantity,
        decimal baseUnitPrice,
        decimal effectiveUnitPrice,
        decimal baseTotal,
        decimal adjustedTotal,
        decimal globalAdjustmentPercent,
        decimal lineAdjustmentPercent,
        Guid rowKey,
        Guid materialId,
        bool isServiceLine)
    {
        Name = name;
        Quantity = quantity;
        BaseUnitPrice = baseUnitPrice;
        EffectiveUnitPrice = effectiveUnitPrice;
        HasPriceOverride = Math.Abs(effectiveUnitPrice - baseUnitPrice) > 0.005m;
        BaseTotal = baseTotal;
        AdjustedTotal = adjustedTotal;
        GlobalAdjustmentPercent = globalAdjustmentPercent;
        LineAdjustmentPercent = lineAdjustmentPercent;
        RowKey = rowKey;
        MaterialId = materialId;
        IsServiceLine = isServiceLine;
    }

    public static ReceiptRowVm ForService(StageWorkTypeLineVm line, decimal globalAdjustmentPercent, decimal globalMultiplier) =>
        new(
            line.Name,
            line.Quantity,
            line.BasePricePerUnit,
            line.PricePerUnit,
            line.LineTotal,
            line.LineTotal * globalMultiplier,
            globalAdjustmentPercent,
            line.LineAdjustmentPercent,
            line.TemplateId,
            Guid.Empty,
            isServiceLine: true);

    public static ReceiptRowVm ForMaterial(StageMaterialLineVm line, decimal globalAdjustmentPercent, decimal globalMultiplier) =>
        new(
            line.MaterialName,
            line.Quantity,
            line.BasePricePerUnit,
            line.PricePerUnit,
            line.LineTotal,
            line.LineTotal * globalMultiplier,
            globalAdjustmentPercent,
            line.LineAdjustmentPercent,
            line.RowId,
            line.MaterialId,
            isServiceLine: false);

    private static string FormatPercent(decimal percent) =>
        percent > 0m ? $"+{percent:N0}%" : percent < 0m ? $"{percent:N0}%" : "0%";
}
