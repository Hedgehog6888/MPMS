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

    private Guid? _projectId;

    public FilesControlViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, IApiService api, IUserSettingsService settings, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _api = api;
        _settings = settings;
        _sync = sync;
        _imagesViewMode = _settings.GetValue("FilesImagesViewMode", "Grid");
        _documentsViewMode = _settings.GetValue("FilesDocumentsViewMode", "List");
    }

    public void Initialize(Guid? projectId = null)
    {
        _projectId = projectId;
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

            if (_projectId.HasValue)
            {
                query = query.Where(f => f.ProjectId == _projectId.Value);
            }

            var files = await query.OrderByDescending(f => f.CreatedAt).ToListAsync();
            
            var projectIds = files.Where(f => f.ProjectId.HasValue).Select(f => f.ProjectId.Value).Distinct().ToList();
            var stageIds = files.Where(f => f.StageId.HasValue).Select(f => f.StageId.Value).Distinct().ToList();
            
            var projects = await db.Projects.Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
            var stages = await db.TaskStages.Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);

            foreach (var f in files)
            {
                if (f.ProjectId.HasValue && projects.TryGetValue(f.ProjectId.Value, out var pname))
                    f.ProjectName = pname;
                if (f.StageId.HasValue && stages.TryGetValue(f.StageId.Value, out var sname))
                    f.StageName = sname;
            }

            AllFiles.Clear();
            foreach (var f in files)
                AllFiles.Add(f);

            UpdateExtensionFilterOptions();
            ApplyFilters();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilters()
    {
        var filtered = AllFiles.AsEnumerable();

        if (CurrentTab == "Images")
        {
            filtered = filtered.Where(f => IsImage(f.FileName));
        }
        else
        {
            filtered = filtered.Where(f => !IsImage(f.FileName));
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
            DisplayedFiles.Add(f);
        }
    }

    private void UpdateExtensionFilterOptions()
    {
        var filtered = AllFiles.AsEnumerable();
        if (CurrentTab == "Images")
        {
            filtered = filtered.Where(f => IsImage(f.FileName));
        }
        else
        {
            filtered = filtered.Where(f => !IsImage(f.FileName));
        }

        var exts = filtered
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
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath)) continue;
                
                var fileInfo = new FileInfo(filePath);
                byte[] fileData = await File.ReadAllBytesAsync(filePath);

                var newFile = new LocalFile
                {
                    Id = Guid.NewGuid(),
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    FileType = fileInfo.Extension,
                    FilePath = filePath,
                    FileData = fileData,
                    ProjectId = _projectId,
                    UploadedById = _auth.UserId ?? Guid.Empty,
                    UploadedByName = _auth.UserName ?? "Unknown",
                    CreatedAt = DateTime.UtcNow,
                    OriginalCreatedAt = fileInfo.CreationTimeUtc
                };

                db.Files.Add(newFile);
                await db.SaveChangesAsync();
                
                // Queue for server upload
                var dto = new FileDto(newFile.Id, newFile.FileName, newFile.FileType ?? "", newFile.FileSize, 
                    newFile.UploadedById, newFile.UploadedByName, newFile.ProjectId, newFile.TaskId, newFile.StageId, 
                    newFile.CreatedAt, newFile.OriginalCreatedAt);
                await _sync.QueueOperationAsync("File", newFile.Id, SyncOperation.Create, dto);

                var logText = _projectId.HasValue ? $"Загружен файл «{newFile.FileName}» в проект" : $"Загружен файл «{newFile.FileName}»";
                await LogActivityAsync(db, logText, "File", newFile.Id, ActivityActionKind.Created);
            }

            await LoadFilesAsync();
            ShowSuccessToast(filePaths.Count() == 1 ? "Файл успешно загружен" : "Файлы успешно загружены");
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
                await LogActivityAsync(db, $"Удалён файл «{file.FileName}»", "File", file.Id, ActivityActionKind.Deleted);
                await LoadFilesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenFile(LocalFile file)
    {
        if (file == null) return;
        
        // Согласно фидбеку: "Смотри откррыватся будут в специально оверлее программы, это будет реализованно чуть позже"
        MessageBox.Show("Открытие файла будет реализовано в специальном оверлее чуть позже.", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
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
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        };

        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        await _sync.QueueLocalActivityLogAsync(log);
    }
}

