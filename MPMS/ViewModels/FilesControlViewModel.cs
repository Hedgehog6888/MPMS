using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Infrastructure;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class FilesControlViewModel : ViewModelBase
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IApiService _api;
    private readonly IUserSettingsService _settings;
    private readonly ISyncService _sync;
    private readonly IPageUiStateStore _uiState;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public event Action<string>? ShowToastRequested;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isFirstLoad = true;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _currentTab = "Images"; // "Images" or "Documents"
    [ObservableProperty] private string _imagesViewMode = "Grid";
    [ObservableProperty] private string _documentsViewMode = "List";
    [ObservableProperty] private string _extensionFilter = "Все";
    [ObservableProperty] private bool _isDraggingOver;
    [ObservableProperty] private bool _isSelectionMode;
    [ObservableProperty] private bool _isWorkerRole;
    [ObservableProperty] private bool _isForemanRole;
    [ObservableProperty] private LocalProject? _project;

    // Фильтры по проектам (отдельные для каждой вкладки)
    [ObservableProperty] private Guid? _projectFilter;
    [ObservableProperty] private Guid? _imagesProjectFilter;
    [ObservableProperty] private Guid? _documentsProjectFilter;
    [ObservableProperty] private ObservableCollection<ProjectFilterOption> _projectFilterOptions = [];
    [ObservableProperty] private bool _isProjectFilterVisible;

    public string ViewMode
    {
        get => CurrentTab == "Images" ? ImagesViewMode : DocumentsViewMode;
        set
        {
            if (CurrentTab == "Images") ImagesViewMode = value;
            else DocumentsViewMode = value;
            OnPropertyChanged(nameof(ViewMode));
        }
    }

    public ObservableCollection<string> ExtensionFilterOptions { get; } = new() { "Все" };

    public ObservableCollection<LocalFile> AllFiles { get; } = new();
    public ObservableCollection<LocalFile> DisplayedFiles { get; } = new();

    public int ImagesCount => AllFiles.Count(f => IsImage(f.FileName));
    public int DocumentsCount => AllFiles.Count(f => !IsImage(f.FileName));
    public int FilesCount => AllFiles.Count;
    public bool AreAllDisplayedFilesSelected => DisplayedFiles.Count > 0 && DisplayedFiles.All(f => f.IsSelected);
    public string ToggleDisplayedSelectionText => AreAllDisplayedFilesSelected ? "Снять выделение" : "Выделить всё";

    private Guid? _projectId;
    private Guid? _taskId;
    private Guid? _stageId;
    private List<LocalFile> _cachedImagesFiles = [];
    private List<LocalFile> _cachedDocumentsFiles = [];
    private Dictionary<Guid, Guid> _stageToProjectMap = []; // StageId -> ProjectId mapping for filtering

    // Тянем только метаданные (без байтов FileData), чтобы первичная загрузка была быстрой.
    // Байты изображений дозагрузим в фоне после показа списка.
    private static readonly System.Linq.Expressions.Expression<Func<LocalFile, LocalFile>> FileMetadataSelector =
        f => new LocalFile
        {
            Id = f.Id,
            FileName = f.FileName,
            FilePath = f.FilePath,
            FileType = f.FileType,
            FileSize = f.FileSize,
            FileData = null,
            UploadedById = f.UploadedById,
            UploadedByName = f.UploadedByName,
            ProjectId = f.ProjectId,
            TaskId = f.TaskId,
            StageId = f.StageId,
            CreatedAt = f.CreatedAt,
            OriginalCreatedAt = f.OriginalCreatedAt,
            Description = f.Description,
            IsSynced = f.IsSynced,
            LastModifiedLocally = f.LastModifiedLocally
        };

    public FilesControlViewModel(
        IDbContextFactory<LocalDbContext> dbFactory,
        IAuthService auth,
        IApiService api,
        IUserSettingsService settings,
        ISyncService sync,
        IPageUiStateStore uiState,
        SidebarFooterViewModel sidebarFooter)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _api = api;
        _settings = settings;
        _sync = sync;
        _uiState = uiState;
        _sidebarFooter = sidebarFooter;
        _imagesViewMode = _settings.GetValue("FilesImagesViewMode", "Grid");
        _documentsViewMode = _settings.GetValue("FilesDocumentsViewMode", "List");
        _isWorkerRole = auth.UserRole == "Worker" || auth.UserRole == "Работник";
        _isForemanRole = auth.UserRole == "Foreman" || auth.UserRole == "Прораб";
        // Для работников скрываем вкладку документов
        if (_isWorkerRole)
        {
            _currentTab = "Images";
        }
    }

    public void RefreshUserInfo()
    {
        var wasWorker = IsWorkerRole;
        IsWorkerRole = _auth.UserRole == "Worker" || _auth.UserRole == "Работник";
        IsForemanRole = _auth.UserRole == "Foreman" || _auth.UserRole == "Прораб";
        
        // Если роль изменилась с работника на не-работника, переключаемся на вкладку Документы
        if (wasWorker && !IsWorkerRole)
        {
            CurrentTab = "Documents";
        }
        // Если роль изменилась с не-работника на работника, переключаемся на вкладку Изображения
        else if (!wasWorker && IsWorkerRole)
        {
            CurrentTab = "Images";
        }
    }

    private PageUiStateBinder Ui => new(_uiState, ResolveUiPageKey());

    private string ResolveUiPageKey()
    {
        if (_projectId is { } pid) return $"{PageUiKeys.Files}:Project:{pid}";
        if (_taskId is { } tid) return $"{PageUiKeys.Files}:Task:{tid}";
        if (_stageId is { } sid) return $"{PageUiKeys.Files}:Stage:{sid}";
        return PageUiKeys.Files;
    }

    public void Initialize(Guid? projectId = null, Guid? taskId = null, Guid? stageId = null)
    {
        _projectId = projectId;
        _taskId = taskId;
        _stageId = stageId;
        IsProjectFilterVisible = !projectId.HasValue && !stageId.HasValue;
        RestorePageUi();
        _ = LoadFilesAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        Ui.SetString(PageUiStateBinder.TabField(CurrentTab, "SearchText"), value);
        ApplyFilters();
    }

    partial void OnCurrentTabChanging(string? oldValue, string newValue)
    {
        IsSelectionMode = false;
        if (!string.IsNullOrEmpty(oldValue))
            SaveTabUi(oldValue);
    }

    partial void OnCurrentTabChanged(string value)
    {
        Ui.SetString("CurrentTab", value);
        if (value == "Images")
            DocumentsProjectFilter = ProjectFilter;
        else
            ImagesProjectFilter = ProjectFilter;

        ProjectFilter = value == "Images" ? ImagesProjectFilter : DocumentsProjectFilter;
        RestoreTabUi(value);

        UpdateExtensionFilterOptions();
        ApplyFilters();
        OnPropertyChanged(nameof(ViewMode));
    }

    partial void OnExtensionFilterChanged(string value)
    {
        Ui.SetString(PageUiStateBinder.TabField(CurrentTab, "ExtensionFilter"), value);
        ApplyFilters();
    }

    partial void OnProjectFilterChanged(Guid? value)
    {
        if (CurrentTab == "Images")
            ImagesProjectFilter = value;
        else
            DocumentsProjectFilter = value;

        Ui.SetGuid(PageUiStateBinder.TabField(CurrentTab, "ProjectFilter"), value);
        ApplyFilters();
    }

    private void SaveTabUi(string tab)
    {
        if (Ui.IsRestoring) return;
        Ui.SetString(PageUiStateBinder.TabField(tab, "SearchText"), SearchText);
        Ui.SetString(PageUiStateBinder.TabField(tab, "ExtensionFilter"), ExtensionFilter);
        Ui.SetGuid(PageUiStateBinder.TabField(tab, "ProjectFilter"),
            tab == "Images" ? ImagesProjectFilter : DocumentsProjectFilter);
    }

    private void RestoreTabUi(string tab)
    {
        SearchText = Ui.GetString(PageUiStateBinder.TabField(tab, "SearchText"));
        ExtensionFilter = Ui.GetString(PageUiStateBinder.TabField(tab, "ExtensionFilter"), "Все");
        var projectId = Ui.GetGuid(PageUiStateBinder.TabField(tab, "ProjectFilter"));
        if (tab == "Images")
            ImagesProjectFilter = projectId;
        else
            DocumentsProjectFilter = projectId;
        ProjectFilter = projectId;
    }

    private void RestorePageUi()
    {
        using var _ = Ui.BeginRestore();
        var tab = Ui.GetString("CurrentTab", "Images");
        CurrentTab = tab == "Documents" ? "Documents" : "Images";
        ImagesProjectFilter = Ui.GetGuid(PageUiStateBinder.TabField("Images", "ProjectFilter"));
        DocumentsProjectFilter = Ui.GetGuid(PageUiStateBinder.TabField("Documents", "ProjectFilter"));
        RestoreTabUi(CurrentTab);
    }

    partial void OnImagesViewModeChanged(string value)
    {
        _settings.SetValue("FilesImagesViewMode", value);
        OnPropertyChanged(nameof(ViewMode));
    }

    partial void OnDocumentsViewModeChanged(string value)
    {
        _settings.SetValue("FilesDocumentsViewMode", value);
        OnPropertyChanged(nameof(ViewMode));
    }

    private sealed record FilesQueryResult(
        List<LocalFile> Files,
        Dictionary<Guid, Guid> StageToProjectMap,
        LocalProject? Project);

    public async Task LoadFilesAsync()
    {
        IsLoading = true;
        try
        {
            // ВАЖНО: провайдер SQLite выполняет запросы синхронно, поэтому всю работу с БД
            // уносим на фоновый поток. Иначе UI-поток блокируется и скелетон не успевает отрисоваться.
            var result = await Task.Run(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var (query, project) = await BuildScopedQueryAsync(db);

                var files = await query
                    .OrderByDescending(f => f.CreatedAt)
                    .Select(FileMetadataSelector)
                    .ToListAsync();

                var stageMap = new Dictionary<Guid, Guid>();
                await EnrichFilesAsync(db, files, stageMap, project);

                // Загружаем превью изображений ДО возврата результата, чтобы карточки появились с фото
                var imageIds = files.Where(f => IsImage(f.FileName)).Select(f => f.Id).ToList();
                if (imageIds.Count > 0)
                {
                    await LoadImagePreviewsSyncAsync(db, files, imageIds);
                }

                return new FilesQueryResult(files, stageMap, project);
            });

            // Применяем результат на UI-потоке.
            Project = result.Project;
            _stageToProjectMap = result.StageToProjectMap;

            AllFiles.Clear();
            foreach (var f in result.Files)
                AllFiles.Add(f);

            _cachedImagesFiles = result.Files.Where(f => IsImage(f.FileName)).ToList();
            _cachedDocumentsFiles = result.Files.Where(f => !IsImage(f.FileName)).ToList();

            RebuildProjectFilterOptions();
            UpdateExtensionFilterOptions();
            ApplyFilters();
            OnPropertyChanged(nameof(ImagesCount));
            OnPropertyChanged(nameof(DocumentsCount));

            _ = _sidebarFooter.RefreshStatsAsync();
        }
        finally
        {
            IsLoading = false;
            IsFirstLoad = false;
        }
    }

    /// <summary>
    /// Инкрементальное обновление: данные уже в памяти, поэтому мы не пересоздаём весь список
    /// (это дорого — элементы не виртуализируются), а только добавляем новые файлы и убираем удалённые.
    /// </summary>
    public async Task RefreshFilesAsync()
    {
        // Первое открытие — нужна полноценная загрузка со скелетоном.
        if (IsFirstLoad)
        {
            await LoadFilesAsync();
            return;
        }

        try
        {
            // Снимок текущих файлов делаем на UI-потоке, дальше работаем с БД в фоне.
            var existingFiles = AllFiles.ToList();
            var existingIds = existingFiles.Select(f => f.Id).ToHashSet();

            var diff = await Task.Run(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var (query, project) = await BuildScopedQueryAsync(db);

                var dbIdSet = (await query.Select(f => f.Id).ToListAsync()).ToHashSet();
                var newIds = dbIdSet.Where(id => !existingIds.Contains(id)).ToList();
                var removedIds = existingIds.Where(id => !dbIdSet.Contains(id)).ToHashSet();

                var stageMap = new Dictionary<Guid, Guid>();
                var newFiles = new List<LocalFile>();
                if (newIds.Count > 0)
                {
                    newFiles = await query
                        .Where(f => newIds.Contains(f.Id))
                        .OrderByDescending(f => f.CreatedAt)
                        .Select(FileMetadataSelector)
                        .ToListAsync();
                    await EnrichFilesAsync(db, newFiles, stageMap, project);
                }

                return (Project: project, NewFiles: newFiles, RemovedIds: removedIds, StageMap: stageMap);
            });

            // Ничего не изменилось — список не трогаем, перерисовки нет.
            if (diff.NewFiles.Count == 0 && diff.RemovedIds.Count == 0)
            {
                _ = _sidebarFooter.RefreshStatsAsync();
                return;
            }

            Project = diff.Project;
            foreach (var kv in diff.StageMap)
                _stageToProjectMap[kv.Key] = kv.Value;

            if (diff.RemovedIds.Count > 0)
            {
                foreach (var rf in existingFiles.Where(f => diff.RemovedIds.Contains(f.Id)))
                {
                    rf.PropertyChanged -= File_PropertyChanged;
                    AllFiles.Remove(rf);
                    _cachedImagesFiles.Remove(rf);
                    _cachedDocumentsFiles.Remove(rf);
                }
            }

            foreach (var nf in diff.NewFiles)
            {
                InsertSortedByCreatedAt(AllFiles, nf);
                if (IsImage(nf.FileName))
                    InsertSortedByCreatedAt(_cachedImagesFiles, nf);
                else
                    InsertSortedByCreatedAt(_cachedDocumentsFiles, nf);
            }

            RebuildProjectFilterOptions();
            UpdateExtensionFilterOptions();
            ApplyFilters();
            OnPropertyChanged(nameof(ImagesCount));
            OnPropertyChanged(nameof(DocumentsCount));
            OnPropertyChanged(nameof(FilesCount));

            var newImageIds = diff.NewFiles.Where(f => IsImage(f.FileName)).Select(f => f.Id).ToList();
            if (newImageIds.Count > 0)
                _ = LoadImagePreviewsAsync(newImageIds);
            _ = _sidebarFooter.RefreshStatsAsync();
        }
        catch
        {
            // Фоновое обновление не критично — если упало, остаёмся на старых данных.
        }
    }

    private static void InsertSortedByCreatedAt<T>(IList<T> list, LocalFile file) where T : LocalFile
    {
        var index = 0;
        while (index < list.Count && list[index].CreatedAt >= file.CreatedAt)
            index++;
        list.Insert(index, (T)(object)file);
    }

    private async Task<(IQueryable<LocalFile> Query, LocalProject? Project)> BuildScopedQueryAsync(LocalDbContext db)
    {
        IQueryable<LocalFile> query = db.Files.AsNoTracking();
        LocalProject? project = null;

        // Приоритет: stageId > taskId > projectId
        if (_stageId.HasValue)
        {
            // Фильтруем только файлы этого этапа
            query = query.Where(f => f.StageId == _stageId.Value);
            // Загружаем проект для отображения имени
            var stage = await db.TaskStages.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == _stageId.Value);
            if (stage != null)
            {
                var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == stage.TaskId);
                if (task != null)
                {
                    project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == task.ProjectId);
                }
            }
        }
        else if (_taskId.HasValue)
        {
            // Фильтруем файлы задачи и файлы этапов задачи
            var taskStageIds = await db.TaskStages.AsNoTracking()
                .Where(s => s.TaskId == _taskId.Value)
                .Select(s => s.Id)
                .ToListAsync();
            query = query.Where(f => f.TaskId == _taskId.Value ||
                (f.StageId.HasValue && taskStageIds.Contains(f.StageId.Value)));
            // Загружаем проект для отображения имени
            var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _taskId.Value);
            if (task != null)
            {
                project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == task.ProjectId);
            }
        }
        else if (_projectId.HasValue)
        {
            project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == _projectId.Value);

            // Получаем все этапы проекта через задачи (как в API)
            var projectTaskIds = await db.Tasks.AsNoTracking()
                .Where(t => t.ProjectId == _projectId.Value)
                .Select(t => t.Id)
                .ToListAsync();
            var projectStageIds = await db.TaskStages.AsNoTracking()
                .Where(s => projectTaskIds.Contains(s.TaskId))
                .Select(s => s.Id)
                .ToListAsync();

            // Фильтруем файлы проекта и файлы этапов проекта
            query = query.Where(f => f.ProjectId == _projectId.Value ||
                (f.StageId.HasValue && projectStageIds.Contains(f.StageId.Value)));
        }
        else
        {
            query = await AvailableFilesQuery.ApplyGlobalFilterAsync(query, db, _auth);
        }

        return (query, project);
    }

    private async Task EnrichFilesAsync(LocalDbContext db, List<LocalFile> files,
        Dictionary<Guid, Guid> stageMap, LocalProject? scopeProject)
    {
        if (files.Count == 0) return;

        // Оптимизация: загружаем проекты и этапы за один раз
        var projectIds = files.Select(f => f.ProjectId).OfType<Guid>().Distinct().ToList();
        var stageIds = files.Select(f => f.StageId).OfType<Guid>().Distinct().ToList();

        var stages = stageIds.Count == 0
            ? new Dictionary<Guid, (string Name, Guid TaskId)>()
            : await db.TaskStages
                .Where(s => stageIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name, s.TaskId })
                .ToDictionaryAsync(s => s.Id, s => (s.Name, s.TaskId));

        var taskIdsFromStages = stages.Values.Select(s => s.TaskId).Distinct().ToList();
        var taskProjectMap = taskIdsFromStages.Count == 0
            ? new Dictionary<Guid, Guid>()
            : await db.Tasks
                .Where(t => taskIdsFromStages.Contains(t.Id))
                .Select(t => new { t.Id, t.ProjectId })
                .ToDictionaryAsync(t => t.Id, t => t.ProjectId);

        foreach (var kvp in stages)
            if (taskProjectMap.TryGetValue(kvp.Value.TaskId, out var pid))
                stageMap[kvp.Key] = pid;

        var allProjectIds = projectIds
            .Concat(stages.Values
                .Where(s => taskProjectMap.ContainsKey(s.TaskId))
                .Select(s => taskProjectMap[s.TaskId]))
            .Distinct()
            .ToList();

        var projects = allProjectIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Projects
                .Where(p => allProjectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name);

        foreach (var f in files)
        {
            if (f.ProjectId.HasValue && projects.TryGetValue(f.ProjectId.Value, out var pname))
                f.ProjectName = pname;
            else if (f.StageId.HasValue
                     && stageMap.TryGetValue(f.StageId.Value, out var derivedProjectId)
                     && projects.TryGetValue(derivedProjectId, out var derivedName))
                f.ProjectName = derivedName;
            else if (_projectId.HasValue && scopeProject != null)
                f.ProjectName = scopeProject.Name;

            if (f.StageId.HasValue && stages.TryGetValue(f.StageId.Value, out var stageInfo))
                f.StageName = stageInfo.Name;
        }
    }

    private void RebuildProjectFilterOptions()
    {
        // Опции фильтра по проектам строим только для общего списка файлов.
        if (_projectId.HasValue || _stageId.HasValue)
            return;

        var currentProjectFilter = ProjectFilter;
        var projectOpts = new List<ProjectFilterOption> { new(null, "Все проекты") };
        projectOpts.AddRange(AllFiles
            .Select(f =>
            {
                var pid = f.ProjectId;
                if (!pid.HasValue && f.StageId.HasValue
                    && _stageToProjectMap.TryGetValue(f.StageId.Value, out var stageProjectId))
                    pid = stageProjectId;

                return new { ProjectId = pid, f.ProjectName };
            })
            .Where(x => x.ProjectId.HasValue && !string.IsNullOrWhiteSpace(x.ProjectName))
            .GroupBy(x => x.ProjectId!.Value)
            .Select(g => new ProjectFilterOption(g.Key, g.First().ProjectName!))
            .OrderBy(p => p.Name));
        ProjectFilterOptions = new ObservableCollection<ProjectFilterOption>(projectOpts);
        if (currentProjectFilter.HasValue && projectOpts.Any(o => o.Id == currentProjectFilter.Value))
            ProjectFilter = currentProjectFilter;
        else if (currentProjectFilter.HasValue)
            ProjectFilter = null;
    }

    private async Task LoadImagePreviewsAsync(List<Guid> imageIds)
    {
        if (imageIds.Count == 0) return;
        try
        {
            // Чтение байтов изображений из SQLite — синхронное и тяжёлое, поэтому выполняем его
            // на фоновом потоке, а присвоение FileData (оно дёргает биндинг миниатюры) маршалим в UI-поток.
            const int batchSize = 12;
            var dispatcher = Application.Current?.Dispatcher;

            await Task.Run(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                for (int i = 0; i < imageIds.Count; i += batchSize)
                {
                    var batch = imageIds.Skip(i).Take(batchSize).ToList();
                    var data = await db.Files.AsNoTracking()
                        .Where(f => batch.Contains(f.Id))
                        .Select(f => new { f.Id, f.FileData })
                        .ToListAsync();

                    if (dispatcher == null)
                        break;

                    // Тяжёлое декодирование миниатюр делаем здесь, на фоновом потоке.
                    // BitmapImage замораживается внутри хелпера, поэтому его можно отдать в UI-поток.
                    var decoded = data
                        .Where(d => d.FileData is { Length: > 0 })
                        .Select(d => new
                        {
                            d.Id,
                            d.FileData,
                            Thumb = MPMS.Services.AvatarHelper.BytesToBitmapImage(d.FileData, 480)
                        })
                        .ToList();

                    await dispatcher.InvokeAsync(() =>
                    {
                        foreach (var d in decoded)
                        {
                            var target = _cachedImagesFiles.FirstOrDefault(x => x.Id == d.Id);
                            if (target == null) continue;
                            if (target.FileData == null)
                                target.FileData = d.FileData; // нужно для открытия полноразмерного фото
                            if (target.Thumbnail == null && d.Thumb != null)
                                target.Thumbnail = d.Thumb; // миниатюра уже декодирована — UI только покажет
                        }
                    });
                }
            });
        }
        catch
        {
            // Превью не критичны для работы — глотаем ошибки.
        }
    }

    private async Task LoadImagePreviewsSyncAsync(LocalDbContext db, List<LocalFile> files, List<Guid> imageIds)
    {
        if (imageIds.Count == 0) return;
        try
        {
            const int batchSize = 12;
            for (int i = 0; i < imageIds.Count; i += batchSize)
            {
                var batch = imageIds.Skip(i).Take(batchSize).ToList();
                var data = await db.Files.AsNoTracking()
                    .Where(f => batch.Contains(f.Id))
                    .Select(f => new { f.Id, f.FileData })
                    .ToListAsync();

                // Декодируем миниатюры и сразу заполняем в списке файлов
                foreach (var d in data.Where(d => d.FileData is { Length: > 0 }))
                {
                    var target = files.FirstOrDefault(x => x.Id == d.Id);
                    if (target == null) continue;
                    target.FileData = d.FileData;
                    target.Thumbnail = MPMS.Services.AvatarHelper.BytesToBitmapImage(d.FileData, 480);
                }
            }
        }
        catch
        {
            // Превью не критичны для работы — глотаем ошибки.
        }
    }

    private void ApplyFilters()
    {
        // Используем кэшированные данные для быстрого переключения вкладок
        var baseList = CurrentTab == "Images" ? _cachedImagesFiles : _cachedDocumentsFiles;
        var filtered = baseList.AsEnumerable();

        // Фильтр по проекту: включаем файлы проекта и файлы этапов проекта
        if (ProjectFilter.HasValue)
        {
            var projectId = ProjectFilter.Value;
            filtered = filtered.Where(f =>
                f.ProjectId == projectId ||
                (f.StageId.HasValue && _stageToProjectMap.TryGetValue(f.StageId.Value, out var pid) && pid == projectId));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var lowerSearch = SearchText.ToLower();
            filtered = filtered.Where(f =>
                f.FileName.ToLower().Contains(lowerSearch) ||
                f.UploadedByName.ToLower().Contains(lowerSearch)
            );
        }

        if (!string.IsNullOrEmpty(ExtensionFilter) && ExtensionFilter != "Все")
        {
            filtered = filtered.Where(f =>
                (Path.GetExtension(f.FileName)?.TrimStart('.').ToUpper() ?? "") == ExtensionFilter);
        }

        DisplayedFiles.Clear();
        foreach (var f in filtered)
        {
            f.PropertyChanged -= File_PropertyChanged;
            f.PropertyChanged += File_PropertyChanged;
            DisplayedFiles.Add(f);
        }
        OnPropertyChanged(nameof(AreAllDisplayedFilesSelected));
        OnPropertyChanged(nameof(ToggleDisplayedSelectionText));
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalFile.IsSelected)) return;
        OnPropertyChanged(nameof(AreAllDisplayedFilesSelected));
        OnPropertyChanged(nameof(ToggleDisplayedSelectionText));
    }

    private void UpdateExtensionFilterOptions()
    {
        // Используем кэшированные данные для быстрого обновления фильтров
        var baseList = CurrentTab == "Images" ? _cachedImagesFiles : _cachedDocumentsFiles;

        var exts = baseList
            .Select(f => Path.GetExtension(f.FileName)?.TrimStart('.').ToUpper() ?? "")
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct()
            .OrderBy(e => e)
            .ToList();

        var oldVal = ExtensionFilter;

        ExtensionFilterOptions.Clear();
        ExtensionFilterOptions.Add("Все");
        foreach (var ext in exts)
            ExtensionFilterOptions.Add(ext);

        if (ExtensionFilterOptions.Contains(oldVal))
            ExtensionFilter = oldVal;
        else
            ExtensionFilter = "Все";
    }

    private bool IsImage(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLower();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp";
    }

    private string GetFileEntityType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLower();
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".svg" };
        var documentExtensions = new[] { ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp" };

        if (imageExtensions.Contains(ext))
            return "Image";
        if (documentExtensions.Contains(ext))
            return "Document";
        return "File";
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        CurrentTab = tab;
    }

    [RelayCommand]
    private void EnterSelectionMode()
    {
        IsSelectionMode = true;
    }

    [RelayCommand]
    private void CancelSelectionMode()
    {
        IsSelectionMode = false;
        foreach (var file in AllFiles)
            file.IsSelected = false;
    }

    [RelayCommand]
    private void ToggleDisplayedSelection()
    {
        var selectAll = !AreAllDisplayedFilesSelected;
        foreach (var file in DisplayedFiles)
            file.IsSelected = selectAll;
        OnPropertyChanged(nameof(AreAllDisplayedFilesSelected));
        OnPropertyChanged(nameof(ToggleDisplayedSelectionText));
    }

    [RelayCommand]
    private async Task SelectionDeleteAsync()
    {
        if (IsWorkerRole || IsForemanRole) return;
        var selectedFiles = AllFiles.Where(f => f.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            ShowToastRequested?.Invoke("Выберите файлы для удаления");
            return;
        }

        var imagesCount = selectedFiles.Count(f => IsImage(f.FileName));
        var documentsCount = selectedFiles.Count - imagesCount;

        var result = Views.ConfirmDeleteDialog.ShowFileDeletionConfirmation(
            Application.Current.MainWindow,
            selectedFiles.Count,
            imagesCount,
            documentsCount);

        if (!result)
            return;

        _sidebarFooter.BeginDelete(selectedFiles.Count);
        string? lastDeletedName = null;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var deletedCount = 0;

            for (var fileIndex = 0; fileIndex < selectedFiles.Count; fileIndex++)
            {
                var file = selectedFiles[fileIndex];
                _sidebarFooter.SetCurrentDeleteFile(file.FileName, fileIndex);

                if (await DeleteFileCoreAsync(db, file))
                {
                    deletedCount++;
                    lastDeletedName = file.FileName;
                    _sidebarFooter.ReportDeleteCompleted(deletedCount);
                }
            }

            await LoadFilesAsync();
            IsSelectionMode = false;
            _sidebarFooter.CompleteDelete(deletedCount, lastDeletedName);
        }
        catch (Exception ex)
        {
            _sidebarFooter.CancelDelete();
            MessageBox.Show($"Ошибка при удалении выбранных файлов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SelectionDownloadAsync()
    {
        var selectedFiles = AllFiles.Where(f => f.IsSelected).ToList();
        if (selectedFiles.Count == 0)
        {
            ShowToastRequested?.Invoke("Выберите файлы для скачивания");
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для сохранения файлов"
        };

        if (dialog.ShowDialog() != true)
            return;

        var savedCount = 0;
        var skippedFiles = new List<string>();

        foreach (var file in selectedFiles)
        {
            try
            {
                var data = await EnsureFileDataAsync(file);
                if (data == null || data.Length == 0)
                {
                    skippedFiles.Add(file.FileName);
                    continue;
                }

                var targetPath = GetUniqueFilePath(dialog.FolderName, file.FileName);
                await File.WriteAllBytesAsync(targetPath, data);
                savedCount++;
            }
            catch
            {
                skippedFiles.Add(file.FileName);
            }
        }

        IsSelectionMode = false;

        if (skippedFiles.Count > 0)
        {
            var message = $"Не удалось сохранить:\n• {string.Join("\n• ", skippedFiles)}";
            MessageBox.Show(message, "Скачивание завершено", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task UploadFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файлы для загрузки",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*|Изображения|*.png;*.jpg;*.jpeg|Документы|*.pdf;*.docx;*.xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            await ProcessFilesInternalAsync(dialog.FileNames);
        }
    }

    [RelayCommand]
    private async Task ProcessFilesAsync(IEnumerable<string> filePaths)
    {
        if (filePaths == null || !filePaths.Any()) return;
        await ProcessFilesInternalAsync(filePaths);
    }

    private async Task ProcessFilesInternalAsync(IEnumerable<string> filePaths)
    {
        var paths = filePaths.Where(File.Exists).ToList();
        if (paths.Count == 0) return;

        var successfullyUploaded = 0;
        var skippedFiles = new List<string>();
        string? lastUploadedName = null;

        _sidebarFooter.BeginUpload(paths.Count);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            for (var fileIndex = 0; fileIndex < paths.Count; fileIndex++)
            {
                var filePath = paths[fileIndex];
                var fileInfo = new FileInfo(filePath);
                _sidebarFooter.SetCurrentFile(fileInfo.Name, fileIndex);

                byte[] fileData;
                try
                {
                    fileData = await File.ReadAllBytesAsync(filePath);
                }
                catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process") ||
                                               ioEx.Message.Contains("used by another process"))
                {
                    skippedFiles.Add(fileInfo.Name);
                    continue;
                }

                // Определяем целевую папку и копируем файл
                var fileId = Guid.NewGuid();
                string targetPath;
                if (IsImage(fileInfo.Name))
                {
                    targetPath = MpmsImagesPaths.EnsureImageCopy(fileId, filePath, fileInfo.Name);
                }
                else
                {
                    targetPath = MpmsDocumentPaths.EnsureDocumentCopy(fileId, filePath, fileInfo.Name);
                }

                var newFile = new LocalFile
                {
                    Id = fileId,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    FileType = fileInfo.Extension,
                    FilePath = targetPath,
                    FileData = fileData,
                    ProjectId = _projectId,
                    TaskId = _taskId,
                    StageId = _stageId,
                    UploadedById = _auth.UserId ?? Guid.Empty,
                    UploadedByName = _auth.UserName ?? "Unknown",
                    CreatedAt = DateTime.UtcNow,
                    OriginalCreatedAt = fileInfo.CreationTimeUtc
                };

                db.Files.Add(newFile);
                await db.SaveChangesAsync();

                var dto = new FileDto(newFile.Id, newFile.FileName, newFile.FileType ?? "", newFile.FileSize,
                    newFile.UploadedById, newFile.UploadedByName, newFile.ProjectId, newFile.TaskId, newFile.StageId,
                    newFile.CreatedAt, newFile.OriginalCreatedAt);
                await _sync.QueueOperationAsync("File", newFile.Id, SyncOperation.Create, dto);

                var entityType = GetFileEntityType(newFile.FileName);
                var entityLabel = entityType switch
                {
                    "Image" => "изображение",
                    "Document" => "документ",
                    _ => "файл"
                };

                string? projectName = null;
                string? taskName = null;
                string? stageName = null;
                if (_projectId.HasValue)
                {
                    var project = await db.Projects.FindAsync(_projectId.Value);
                    projectName = project?.Name;
                }
                if (_taskId.HasValue)
                {
                    var task = await db.Tasks.FindAsync(_taskId.Value);
                    taskName = task?.Name;
                }
                if (_stageId.HasValue)
                {
                    var stage = await db.TaskStages.FindAsync(_stageId.Value);
                    stageName = stage?.Name;
                }

                string locationText;
                if (stageName != null && projectName != null)
                    locationText = $"в этап «{stageName}» проекта «{projectName}»";
                else if (taskName != null && projectName != null)
                    locationText = $"в задачу «{taskName}» проекта «{projectName}»";
                else if (projectName != null)
                    locationText = $"в проект «{projectName}»";
                else
                    locationText = "";

                var logText = string.IsNullOrEmpty(locationText)
                    ? $"Загружен {entityLabel} «{newFile.FileName}»"
                    : $"Загружен {entityLabel} «{newFile.FileName}» {locationText}";
                await LogActivityAsync(db, logText, entityType, newFile.Id, ActivityActionKind.Created);

                await EnrichAndAddFileToUiAsync(db, newFile, projectName, stageName);
                successfullyUploaded++;
                lastUploadedName = newFile.FileName;
                _sidebarFooter.ReportFileCompleted(successfullyUploaded);
            }

            if (skippedFiles.Count > 0)
            {
                var skippedList = string.Join("\n• ", skippedFiles);
                MessageBox.Show(
                    $"Следующие файлы не были загружены, так как они открыты в другой программе:\n\n• {skippedList}\n\nЗакройте файлы и попробуйте снова.",
                    "Файлы пропущены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (successfullyUploaded > 0)
                _sidebarFooter.CompleteUpload(successfullyUploaded, lastUploadedName);
            else
                _sidebarFooter.CancelUpload();
        }
        catch (Exception ex)
        {
            _sidebarFooter.CancelUpload();
            var msg = ex.Message;
            if (ex.InnerException != null) msg += $"\nInner: {ex.InnerException.Message}";
            MessageBox.Show($"Ошибка при загрузке файлов: {msg}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EnrichAndAddFileToUiAsync(LocalDbContext db, LocalFile newFile, string? projectName, string? stageName)
    {
        if (!string.IsNullOrEmpty(projectName))
            newFile.ProjectName = projectName;
        else if (_projectId.HasValue && Project != null)
            newFile.ProjectName = Project.Name;
        else if (newFile.ProjectId.HasValue)
        {
            var p = await db.Projects.AsNoTracking()
                .Where(x => x.Id == newFile.ProjectId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();
            if (p != null) newFile.ProjectName = p;
        }

        if (!string.IsNullOrEmpty(stageName))
            newFile.StageName = stageName;
        else if (newFile.StageId.HasValue)
        {
            var stage = await db.TaskStages.AsNoTracking()
                .Where(s => s.Id == newFile.StageId.Value)
                .Select(s => new { s.Name, s.TaskId })
                .FirstOrDefaultAsync();
            if (stage != null)
            {
                newFile.StageName = stage.Name;
                var taskProjectId = await db.Tasks.AsNoTracking()
                    .Where(t => t.Id == stage.TaskId)
                    .Select(t => t.ProjectId)
                    .FirstOrDefaultAsync();
                if (taskProjectId != Guid.Empty)
                {
                    _stageToProjectMap[newFile.StageId.Value] = taskProjectId;
                    if (string.IsNullOrEmpty(newFile.ProjectName))
                    {
                        var pname = await db.Projects.AsNoTracking()
                            .Where(p => p.Id == taskProjectId)
                            .Select(p => p.Name)
                            .FirstOrDefaultAsync();
                        if (pname != null) newFile.ProjectName = pname;
                    }
                }
            }
        }

        AllFiles.Insert(0, newFile);
        if (IsImage(newFile.FileName))
            _cachedImagesFiles.Insert(0, newFile);
        else
            _cachedDocumentsFiles.Insert(0, newFile);

        if (!_projectId.HasValue && !_stageId.HasValue && newFile.ProjectId.HasValue
            && !string.IsNullOrWhiteSpace(newFile.ProjectName))
        {
            if (!ProjectFilterOptions.Any(o => o.Id == newFile.ProjectId))
            {
                var opts = ProjectFilterOptions.ToList();
                opts.Add(new ProjectFilterOption(newFile.ProjectId, newFile.ProjectName));
                ProjectFilterOptions = new ObservableCollection<ProjectFilterOption>(
                    opts.OrderBy(o => o.Id == null).ThenBy(o => o.Name));
            }
        }

        UpdateExtensionFilterOptions();
        ApplyFilters();
        OnPropertyChanged(nameof(ImagesCount));
        OnPropertyChanged(nameof(DocumentsCount));
        OnPropertyChanged(nameof(FilesCount));

        if (IsImage(newFile.FileName))
            _ = LoadImagePreviewsAsync([newFile.Id]);
    }

    [RelayCommand]
    private async Task DeleteFileAsync(LocalFile file)
    {
        if (file == null) return;
        if (IsWorkerRole || IsForemanRole) return;

        var owner = Application.Current.MainWindow;
        if (!MPMS.Views.ConfirmDeleteDialog.Show(owner, "Файл", file.FileName))
            return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            if (await DeleteFileCoreAsync(db, file))
                await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<bool> DeleteFileCoreAsync(LocalDbContext db, LocalFile file)
    {
        var dbFile = await db.Files.FindAsync(file.Id);
        if (dbFile == null)
            return false;

        if (dbFile.IsSynced)
            await _sync.QueueOperationAsync("File", file.Id, SyncOperation.Delete, new { });

        // Удаляем физический файл с диска
        if (!string.IsNullOrEmpty(dbFile.FilePath) && File.Exists(dbFile.FilePath))
        {
            try
            {
                File.Delete(dbFile.FilePath);
            }
            catch (Exception ex)
            {
                // Логируем ошибку, но не прерываем удаление из БД
                System.Diagnostics.Debug.WriteLine($"Ошибка при удалении файла с диска: {ex.Message}");
            }
        }

        db.Files.Remove(dbFile);
        var entityType = GetFileEntityType(file.FileName);
        var entityLabel = entityType switch
        {
            "Image" => "изображение",
            "Document" => "документ",
            _ => "файл"
        };

        string? projectName = null;
        string? taskName = null;
        string? stageName = null;
        if (dbFile.ProjectId.HasValue)
        {
            var project = await db.Projects.FindAsync(dbFile.ProjectId.Value);
            projectName = project?.Name;
        }
        if (dbFile.TaskId.HasValue)
        {
            var task = await db.Tasks.FindAsync(dbFile.TaskId.Value);
            taskName = task?.Name;
        }
        if (dbFile.StageId.HasValue)
        {
            var stage = await db.TaskStages.FindAsync(dbFile.StageId.Value);
            stageName = stage?.Name;
        }

        string locationText;
        if (stageName != null && projectName != null)
            locationText = $"из этапа «{stageName}» проекта «{projectName}»";
        else if (taskName != null && projectName != null)
            locationText = $"из задачи «{taskName}» проекта «{projectName}»";
        else if (projectName != null)
            locationText = $"из проекта «{projectName}»";
        else
            locationText = "";

        var logText = string.IsNullOrEmpty(locationText)
            ? $"Удалён {entityLabel} «{file.FileName}»"
            : $"Удалён {entityLabel} «{file.FileName}» {locationText}";
        await LogActivityAsync(db, logText, entityType, file.Id, ActivityActionKind.Deleted);
        return true;
    }

    [RelayCommand]
    private async Task OpenPhotoViewer(LocalFile file)
    {
        if (file == null) return;

        // Проверяем, что это изображение - если нет, открываем в системном приложении
        if (!IsImage(file.FileName))
        {
            await OpenFile(file);
            return;
        }

        string filePath = string.Empty;

        // Если файл существует на диске, копируем в MPMS/images и открываем копию
        if (!string.IsNullOrEmpty(file.FilePath) && File.Exists(file.FilePath))
        {
            var mpmsPath = MpmsImagesPaths.EnsureImageCopy(file.Id, file.FilePath, file.FileName);
            filePath = mpmsPath;
        }
        // Иначе извлекаем из FileData в MPMS/images
        else if (file.FileData != null && file.FileData.Length > 0)
        {
            filePath = await EnsureImageFileAsync(file);
        }
        else if (_api.IsOnline)
        {
            IsLoading = true;
            try
            {
                var data = await _api.DownloadFileAsync(file.Id);
                if (data != null)
                {
                    file.FileData = data;
                    filePath = await EnsureImageFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке файла: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally { IsLoading = false; }
        }

        // Загружаем данные аватара загрузчика
        byte[]? uploaderAvatarData = null;
        string? uploaderAvatarPath = null;
        if (file.UploadedById != Guid.Empty)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var uploader = await db.Users.Where(u => u.Id == file.UploadedById)
                    .Select(u => new { u.AvatarData, u.AvatarPath })
                    .FirstOrDefaultAsync();
                if (uploader != null)
                {
                    uploaderAvatarData = uploader.AvatarData;
                    uploaderAvatarPath = uploader.AvatarPath;
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            MainWindow.Instance?.ShowPhotoViewer(filePath, file.FileName, file.Description, file.UploadedByName, file.UploadedById, uploaderAvatarData, uploaderAvatarPath, file.ProjectId ?? Guid.Empty,
                (savedPath, savedFileName, savedDescription) => SaveEditedPhotoAsync(file.Id, savedPath, savedFileName, savedDescription, filePath));
        }
        else
        {
            MessageBox.Show("Данные файла отсутствуют локально, а сервер недоступен.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task OpenFromList(LocalFile file)
    {
        if (file == null) return;

        if (IsImage(file.FileName))
            await OpenPhotoViewer(file);
        else
            await OpenFile(file);
    }

    [RelayCommand]
    private async Task OpenFile(LocalFile file)
    {
        if (file == null) return;

        string filePath = string.Empty;

        if (!string.IsNullOrEmpty(file.FilePath) && File.Exists(file.FilePath))
        {
            filePath = file.FilePath;
        }

        else if (file.FileData != null && file.FileData.Length > 0)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"mpms_open_{file.Id}");
            Directory.CreateDirectory(tempDir);
            filePath = Path.Combine(tempDir, file.FileName);
            await File.WriteAllBytesAsync(filePath, file.FileData);
        }

        else if (_api.IsOnline)
        {
            IsLoading = true;
            try
            {
                var data = await _api.DownloadFileAsync(file.Id);
                if (data != null)
                {
                    file.FileData = data;
                    var tempDir = Path.Combine(Path.GetTempPath(), $"mpms_open_{file.Id}");
                    Directory.CreateDirectory(tempDir);
                    filePath = Path.Combine(tempDir, file.FileName);
                    await File.WriteAllBytesAsync(filePath, data);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке файла: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally { IsLoading = false; }
        }

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии файла: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Данные файла отсутствуют локально, а сервер недоступен.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task OpenDocumentViewer(LocalFile file)
    {
        if (file == null) return;

        // Проверяем, поддерживается ли тип документа
        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        bool isDocument = ext == ".txt" || ext == ".csv" || ext == ".log" || ext == ".json" || ext == ".xml" ||
                        ext == ".md" || ext == ".html" || ext == ".htm" ||
                        ext == ".doc" || ext == ".docx" || ext == ".docm" || ext == ".dot" || ext == ".dotx" ||
                        ext == ".xls" || ext == ".xlsx" || ext == ".xlsm" || ext == ".xlsb";

        if (!isDocument)
        {
            MessageBox.Show("Этот тип файла не поддерживается для просмотра в приложении.", "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string docPath = string.Empty;

        // Если файл существует на диске, копируем в MPMS/documents и открываем копию
        if (!string.IsNullOrEmpty(file.FilePath) && File.Exists(file.FilePath))
        {
            var mpmsPath = MpmsDocumentPaths.EnsureDocumentCopy(file.Id, file.FilePath, file.FileName);
            docPath = mpmsPath;
        }
        // Иначе извлекаем из FileData в MPMS/documents
        else if (file.FileData != null && file.FileData.Length > 0)
        {
            docPath = await EnsureDocumentFileAsync(file);
        }

        else if (_api.IsOnline)
        {
            IsLoading = true;
            try
            {
                var data = await _api.DownloadFileAsync(file.Id);
                if (data != null)
                {
                    file.FileData = data;
                    docPath = await EnsureDocumentFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке файла: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { IsLoading = false; }
        }

        if (!string.IsNullOrEmpty(docPath) && File.Exists(docPath))
        {
            MainWindow.Instance?.ShowDocumentViewer(docPath, file.FileName, file.Description,
                (savedPath, savedFileName, savedDescription) => SaveEditedDocumentAsync(file.Id, savedPath, savedFileName, savedDescription, docPath));
        }
        else
        {
            MessageBox.Show("Не удалось загрузить файл для просмотра.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<string> EnsureDocumentFileAsync(LocalFile file)
    {
        var mpmsPath = MpmsDocumentPaths.GetDocumentFilePath(file.Id, file.FileName);
        if (File.Exists(mpmsPath))
            return mpmsPath;

        if (file.FileData != null && file.FileData.Length > 0)
        {
            Directory.CreateDirectory(MpmsDocumentPaths.GetDocumentsDirectory());
            await File.WriteAllBytesAsync(mpmsPath, file.FileData);
            return mpmsPath;
        }

        return string.Empty;
    }

    private async Task<string> EnsureImageFileAsync(LocalFile file)
    {
        var mpmsPath = MpmsImagesPaths.GetImageFilePath(file.Id, file.FileName);
        if (File.Exists(mpmsPath))
            return mpmsPath;

        if (file.FileData != null && file.FileData.Length > 0)
        {
            Directory.CreateDirectory(MpmsImagesPaths.GetImagesDirectory());
            await File.WriteAllBytesAsync(mpmsPath, file.FileData);
            return mpmsPath;
        }

        return string.Empty;
    }

    private async Task SaveEditedDocumentAsync(Guid fileId, string savedPath, string savedFileName, string? savedDescription, string mpmsPath)
    {
        if (!File.Exists(savedPath)) return;

        var fileInfo = new FileInfo(savedPath);
        var fileData = await File.ReadAllBytesAsync(savedPath);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var dbFile = await db.Files.FindAsync(fileId);
        if (dbFile is null) return;

        dbFile.FileName = savedFileName;
        dbFile.FileType = fileInfo.Extension;
        dbFile.FileSize = fileInfo.Length;
        dbFile.FileData = fileData;
        dbFile.Description = savedDescription;

        // Копируем отредактированный файл в папку MPMS/documents
        var targetPath = MpmsDocumentPaths.GetDocumentFilePath(fileId, savedFileName);
        Directory.CreateDirectory(MpmsDocumentPaths.GetDocumentsDirectory());
        File.Copy(savedPath, targetPath, true);
        dbFile.FilePath = targetPath;

        dbFile.IsSynced = false;
        dbFile.LastModifiedLocally = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await LoadFilesAsync();
    }

    private async Task SaveEditedPhotoAsync(Guid fileId, string savedPath, string savedFileName, string? savedDescription, string mpmsPath)
    {
        if (!File.Exists(savedPath)) return;

        var fileInfo = new FileInfo(savedPath);
        var fileData = await File.ReadAllBytesAsync(savedPath);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var dbFile = await db.Files.FindAsync(fileId);
        if (dbFile is null) return;

        dbFile.FileName = savedFileName;
        dbFile.FileType = fileInfo.Extension;
        dbFile.FileSize = fileInfo.Length;
        dbFile.FileData = fileData;
        dbFile.Description = savedDescription;

        // Копируем отредактированный файл в папку MPMS/images
        var targetPath = MpmsImagesPaths.GetImageFilePath(fileId, savedFileName);
        if (!string.Equals(savedPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(MpmsImagesPaths.GetImagesDirectory());
            File.Copy(savedPath, targetPath, true);
        }
        dbFile.FilePath = targetPath;

        dbFile.IsSynced = false;
        dbFile.LastModifiedLocally = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task DownloadFileAsync(LocalFile file)
    {
        if (file == null) return;

        var data = await EnsureFileDataAsync(file);
        if (data == null || data.Length == 0)
            return;

        var dialog = new SaveFileDialog
        {
            FileName = file.FileName,
            DefaultExt = Path.GetExtension(file.FileName),
            Filter = $"Файлы ({Path.GetExtension(file.FileName)})|*{Path.GetExtension(file.FileName)}|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await File.WriteAllBytesAsync(dialog.FileName, data);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async Task<byte[]?> EnsureFileDataAsync(LocalFile file)
    {
        if (file.FileData != null && file.FileData.Length > 0)
            return file.FileData;

        if (!_api.IsOnline)
        {
            MessageBox.Show("Данные файла отсутствуют локально, а сервер недоступен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        IsLoading = true;
        try
        {
            var data = await _api.DownloadFileAsync(file.Id);
            if (data == null || data.Length == 0)
            {
                MessageBox.Show($"Не удалось скачать файл «{file.FileName}» с сервера.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            file.FileData = data;
            await using var db = await _dbFactory.CreateDbContextAsync();
            var dbFile = await db.Files.FindAsync(file.Id);
            if (dbFile != null)
            {
                dbFile.FileData = data;
                await db.SaveChangesAsync();
            }

            return data;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при скачивании файла «{file.FileName}»: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string GetUniqueFilePath(string folderPath, string fileName)
    {
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = "file";

        var basePath = Path.Combine(folderPath, safeFileName);
        if (!File.Exists(basePath))
            return basePath;

        var name = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        var index = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(folderPath, $"{name} ({index}){extension}");
            index++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId, string? actionType = null)
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
            UserColor = "#1B6EC2",
            ActionType = actionType,
            ActionText = actionText,
            DetailsText = ActivityDetailsService.BuildGenericDetails(actionText, entityType, actionType),
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        };

        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        await _sync.QueueLocalActivityLogAsync(log);
    }
}
