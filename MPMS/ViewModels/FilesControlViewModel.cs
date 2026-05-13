using System;
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
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class FilesControlViewModel : ViewModelBase
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IApiService _api;
    private readonly IUserSettingsService _settings;
    private readonly ISyncService _sync;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _currentTab = "Images"; // "Images" or "Documents"
    [ObservableProperty] private string _imagesViewMode = "Grid";
    [ObservableProperty] private string _documentsViewMode = "List";
    [ObservableProperty] private string _extensionFilter = "Все";
    [ObservableProperty] private bool _isDraggingOver;
    [ObservableProperty] private bool _isSuccessToastVisible;
    [ObservableProperty] private string _successToastMessage = string.Empty;
    [ObservableProperty] private bool _isWorkerRole;
    [ObservableProperty] private LocalProject? _project;

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

    private Guid? _projectId;
    private Guid? _taskId;
    private Guid? _stageId;
    private List<LocalFile> _cachedImagesFiles = new();
    private List<LocalFile> _cachedDocumentsFiles = new();

    public FilesControlViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, IApiService api, IUserSettingsService settings, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _api = api;
        _settings = settings;
        _sync = sync;
        _imagesViewMode = _settings.GetValue("FilesImagesViewMode", "Grid");
        _documentsViewMode = _settings.GetValue("FilesDocumentsViewMode", "List");
        _isWorkerRole = auth.UserRole == "Worker" || auth.UserRole == "Работник";
        // Для работников скрываем вкладку документов
        if (_isWorkerRole)
        {
            _currentTab = "Images";
        }
    }

    public void Initialize(Guid? projectId = null, Guid? taskId = null, Guid? stageId = null)
    {
        _projectId = projectId;
        _taskId = taskId;
        _stageId = stageId;
        _ = LoadFilesAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnCurrentTabChanged(string value)
    {
        UpdateExtensionFilterOptions();
        ApplyFilters();
        OnPropertyChanged(nameof(ViewMode));
    }

    partial void OnExtensionFilterChanged(string value)
    {
        ApplyFilters();
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

    public async Task LoadFilesAsync()
    {
        IsLoading = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            IQueryable<LocalFile> query = db.Files;

            // Загружаем проект, если указан projectId
            if (_projectId.HasValue)
            {
                Project = await db.Projects.FindAsync(_projectId.Value);
                query = query.Where(f => f.ProjectId == _projectId.Value);
            }
            else
            {
                Project = null;
                // Фильтрация по ролям
                var userRole = _auth.UserRole;
                var userId = _auth.UserId;

                if (userRole == "Administrator" || userRole == "Admin")
                {
                    // Админ видит все файлы
                }
                else if (userRole == "Worker" || userRole == "Работник")
                {
                    var userStageIds = db.StageAssignees
                        .Where(sa => sa.UserId == userId)
                        .Select(sa => sa.StageId)
                        .ToList();

                    query = query.Where(f =>
                        IsImage(f.FileName) &&
                        f.StageId.HasValue &&
                        userStageIds.Contains(f.StageId.Value));
                }
                else
                {
                    var userProjectIds = db.ProjectMembers
                        .Where(pm => pm.UserId == userId)
                        .Select(pm => pm.ProjectId)
                        .ToList();

                    query = query.Where(f =>
                        !f.ProjectId.HasValue || 
                        userProjectIds.Contains(f.ProjectId.Value));
                }
            }

            var files = await query.OrderByDescending(f => f.CreatedAt).ToListAsync();

            // Оптимизация: загружаем проекты и stages за один раз
            var projectIds = files.Select(f => f.ProjectId).OfType<Guid>().Distinct().ToList();
            var stageIds = files.Select(f => f.StageId).OfType<Guid>().Distinct().ToList();

            var projects = await db.Projects
                .Where(p => projectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var stages = await db.TaskStages
                .Where(s => stageIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(s => s.Id, s => s.Name);

            foreach (var f in files)
            {
                if (f.ProjectId.HasValue && projects.TryGetValue(f.ProjectId.Value, out var pname))
                    f.ProjectName = pname;
                if (f.StageId.HasValue && stages.TryGetValue(f.StageId.Value, out var sname))
                    f.StageName = sname;
            }

            // Оптимизация: добавляем все сразу вместо по одному
            AllFiles.Clear();
            foreach (var f in files)
                AllFiles.Add(f);

            _cachedImagesFiles = files.Where(f => IsImage(f.FileName)).ToList();
            _cachedDocumentsFiles = files.Where(f => !IsImage(f.FileName)).ToList();

            UpdateExtensionFilterOptions();
            ApplyFilters();
            OnPropertyChanged(nameof(ImagesCount));
            OnPropertyChanged(nameof(DocumentsCount));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        // Используем кэшированные данные для быстрого переключения вкладок
        var baseList = CurrentTab == "Images" ? _cachedImagesFiles : _cachedDocumentsFiles;
        var filtered = baseList.AsEnumerable();

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
            DisplayedFiles.Add(f);
        }
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
        IsLoading = true;
        var successfullyUploaded = 0;
        var skippedFiles = new List<string>();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath)) continue;

                var fileInfo = new FileInfo(filePath);
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

                var newFile = new LocalFile
                {
                    Id = Guid.NewGuid(),
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    FileType = fileInfo.Extension,
                    FilePath = filePath,
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

                // Получаем названия проекта, задачи и этапа для лога
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

                successfullyUploaded++;
            }

            await LoadFilesAsync();

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
            {
                ShowSuccessToast(successfullyUploaded == 1 ? "Файл успешно загружен" : "Файлы успешно загружены");
            }
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (ex.InnerException != null) msg += $"\nInner: {ex.InnerException.Message}";
            MessageBox.Show($"Ошибка при загрузке файлов: {msg}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteFileAsync(LocalFile file)
    {
        if (file == null) return;

        var owner = Application.Current.MainWindow;
        if (!MPMS.Views.ConfirmDeleteDialog.Show(owner, "Файл", file.FileName))
            return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var dbFile = await db.Files.FindAsync(file.Id);
            if (dbFile != null)
            {
                if (dbFile.IsSynced)
                {
                    await _sync.QueueOperationAsync("File", file.Id, SyncOperation.Delete, new { });
                }
                db.Files.Remove(dbFile);
                var entityType = GetFileEntityType(file.FileName);
                var entityLabel = entityType switch
                {
                    "Image" => "изображение",
                    "Document" => "документ",
                    _ => "файл"
                };

                // Получаем названия проекта, задачи и этапа для лога
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
                await LoadFilesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        if (!string.IsNullOrEmpty(file.FilePath) && File.Exists(file.FilePath))
        {
            filePath = file.FilePath;
        }

        else if (file.FileData != null && file.FileData.Length > 0)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"mpms_photo_{file.Id}");
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
                    var tempDir = Path.Combine(Path.GetTempPath(), $"mpms_photo_{file.Id}");
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
        dbFile.FilePath = mpmsPath;
        dbFile.IsSynced = false;
        dbFile.LastModifiedLocally = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await LoadFilesAsync();
        ShowSuccessToast("Документ сохранен");
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
        dbFile.FilePath = mpmsPath;
        dbFile.IsSynced = false;
        dbFile.LastModifiedLocally = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await LoadFilesAsync();
        ShowSuccessToast("Фото сохранено");
    }

    [RelayCommand]
    private async Task DownloadFileAsync(LocalFile file)
    {
        if (file == null) return;

        if (file.FileData == null || file.FileData.Length == 0)
        {
            if (!_api.IsOnline)
            {
                MessageBox.Show("Данные файла отсутствуют локально, а сервер недоступен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsLoading = true;
            try
            {
                var data = await _api.DownloadFileAsync(file.Id);
                if (data != null)
                {
                    file.FileData = data;
                    await using var db = await _dbFactory.CreateDbContextAsync();
                    var dbFile = await db.Files.FindAsync(file.Id);
                    if (dbFile != null)
                    {
                        dbFile.FileData = data;
                        await db.SaveChangesAsync();
                    }
                }
                else
                {
                    MessageBox.Show("Не удалось скачать файл с сервера.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при скачивании: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally { IsLoading = false; }
        }

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
                await File.WriteAllBytesAsync(dialog.FileName, file.FileData);
                ShowSuccessToast("Файл успешно сохранён");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void ShowSuccessToast(string message)
    {
        SuccessToastMessage = message;
        IsSuccessToastVisible = true;
        await Task.Delay(7000);
        IsSuccessToastVisible = false;
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
