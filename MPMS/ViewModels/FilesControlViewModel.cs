using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class FilesControlViewModel : ViewModelBase
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IUserSettingsService _settings;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _currentTab = "Images"; // "Images" or "Documents"
    [ObservableProperty] private string _imagesViewMode = "Grid";
    [ObservableProperty] private string _documentsViewMode = "List";
    [ObservableProperty] private string _extensionFilter = "Все";

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

    public FilesControlViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, IUserSettingsService settings)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _settings = settings;
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
            filtered = filtered.Where(f => f.FileType?.ToLower() == ExtensionFilter);
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

        var exts = filtered.Select(f => f.FileType?.ToLower() ?? "").Where(e => !string.IsNullOrEmpty(e)).Distinct().OrderBy(e => e).ToList();
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
            IsLoading = true;
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();

                foreach (var filePath in dialog.FileNames)
                {
                    var fileInfo = new FileInfo(filePath);
                    byte[] fileData = await File.ReadAllBytesAsync(filePath);

                    var newFile = new LocalFile
                    {
                        Id = Guid.NewGuid(),
                        FileName = fileInfo.Name,
                        FileSize = fileInfo.Length,
                        FileType = fileInfo.Extension,
                        FilePath = filePath, // Оригинальный путь (для справки)
                        FileData = fileData, // Сохраняем в БД как просили
                        ProjectId = _projectId,
                        UploadedById = _auth.UserId ?? Guid.Empty,
                        UploadedByName = _auth.UserName ?? "Unknown",
                        CreatedAt = DateTime.UtcNow
                    };

                    db.Files.Add(newFile);
                }

                await db.SaveChangesAsync();
                await LoadFilesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке файлов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteFileAsync(LocalFile file)
    {
        if (file == null) return;
        
        var owner = Application.Current.MainWindow;
        if (!MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(owner, "Файл", file.FileName))
            return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var dbFile = await db.Files.FindAsync(file.Id);
            if (dbFile != null)
            {
                db.Files.Remove(dbFile);
                await db.SaveChangesAsync();
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
}
