using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Services;

namespace MPMS.ViewModels;

public enum SidebarFooterMode
{
    Stats,
    Uploading,
    UploadSummary
}

public partial class SidebarFooterViewModel : ViewModelBase
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private DispatcherTimer? _revertToStatsTimer;

    [ObservableProperty] private SidebarFooterMode _mode = SidebarFooterMode.Stats;
    [ObservableProperty] private int _statsImages;
    [ObservableProperty] private int _statsDocuments;
    [ObservableProperty] private int _statsReports;
    [ObservableProperty] private int _uploadCompleted;
    [ObservableProperty] private int _uploadTotal;
    [ObservableProperty] private string _uploadCurrentFileName = string.Empty;
    [ObservableProperty] private string _uploadSummaryTitle = string.Empty;
    [ObservableProperty] private bool _isSingleFileUpload;
    [ObservableProperty] private double _uploadProgressPercent;

    public bool IsStatsMode => Mode == SidebarFooterMode.Stats;
    public bool IsUploadingMode => Mode == SidebarFooterMode.Uploading;
    public bool IsUploadSummaryMode => Mode == SidebarFooterMode.UploadSummary;

    public string UploadProgressTitle =>
        IsSingleFileUpload
            ? "Загружается файл"
            : $"Загружается {UploadCompleted} из {UploadTotal} {FilesGenitive(UploadTotal)}";

    public string UploadProgressTitleShort =>
        IsSingleFileUpload ? "Файл" : $"{UploadCompleted}/{UploadTotal}";

    public string UploadCurrentFileNameDisplay =>
        TruncateFileName(UploadCurrentFileName, 18);

    public int StatsTotalFiles => StatsImages + StatsDocuments + StatsReports;

    public SidebarFooterViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _auth = auth;
    }

    partial void OnModeChanged(SidebarFooterMode value)
    {
        OnPropertyChanged(nameof(IsStatsMode));
        OnPropertyChanged(nameof(IsUploadingMode));
        OnPropertyChanged(nameof(IsUploadSummaryMode));
    }

    partial void OnUploadCompletedChanged(int value)
    {
        UpdateProgressPercent();
        OnPropertyChanged(nameof(UploadProgressTitle));
        OnPropertyChanged(nameof(UploadProgressTitleShort));
    }

    partial void OnUploadTotalChanged(int value)
    {
        UpdateProgressPercent();
        OnPropertyChanged(nameof(UploadProgressTitle));
        OnPropertyChanged(nameof(UploadProgressTitleShort));
    }

    partial void OnIsSingleFileUploadChanged(bool value)
    {
        OnPropertyChanged(nameof(UploadProgressTitle));
        OnPropertyChanged(nameof(UploadProgressTitleShort));
    }

    partial void OnUploadCurrentFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(UploadCurrentFileNameDisplay));
    }

    partial void OnStatsImagesChanged(int value) => OnPropertyChanged(nameof(StatsTotalFiles));
    partial void OnStatsDocumentsChanged(int value) => OnPropertyChanged(nameof(StatsTotalFiles));
    partial void OnStatsReportsChanged(int value) => OnPropertyChanged(nameof(StatsTotalFiles));

    private void UpdateProgressPercent()
    {
        UploadProgressPercent = UploadTotal > 0
            ? Math.Round(100.0 * UploadCompleted / UploadTotal, 1)
            : 0;
    }

    public async Task RefreshStatsAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var query = await AvailableFilesQuery.ApplyGlobalFilterAsync(
                db.Files.AsNoTracking(), db, _auth);

            var fileNames = await query.Select(f => f.FileName).ToListAsync();
            StatsImages = fileNames.Count(AvailableFilesQuery.IsImageFileName);
            StatsDocuments = fileNames.Count - StatsImages;
            StatsReports = 0;
        }
        catch
        {
            // Статистика не критична для работы приложения.
        }
    }

    public void BeginUpload(int total)
    {
        _revertToStatsTimer?.Stop();
        _revertToStatsTimer = null;
        Mode = SidebarFooterMode.Uploading;
        UploadTotal = total;
        UploadCompleted = 0;
        IsSingleFileUpload = total == 1;
        UploadCurrentFileName = string.Empty;
        UploadSummaryTitle = string.Empty;
        UploadProgressPercent = 0;
    }

    public void SetCurrentFile(string fileName, int fileIndexZeroBased = 0)
    {
        UploadCurrentFileName = fileName;
    }

    public void ReportFileCompleted(int completed)
    {
        UploadCompleted = completed;
    }

    public void CompleteUpload(int uploadedCount, string? lastFileName)
    {
        if (uploadedCount <= 0)
        {
            Mode = SidebarFooterMode.Stats;
            return;
        }

        IsSingleFileUpload = uploadedCount == 1;
        UploadCompleted = uploadedCount;
        UploadTotal = uploadedCount;
        UploadProgressPercent = 100;

        if (uploadedCount == 1 && !string.IsNullOrWhiteSpace(lastFileName))
        {
            UploadCurrentFileName = lastFileName;
            UploadSummaryTitle = "Файл загружен";
        }
        else
        {
            UploadCurrentFileName = string.Empty;
            UploadSummaryTitle = $"Загружено {uploadedCount} {FilesNominative(uploadedCount)}";
        }

        Mode = SidebarFooterMode.UploadSummary;
        _ = RefreshStatsAsync();

        _revertToStatsTimer?.Stop();
        _revertToStatsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _revertToStatsTimer.Tick += OnRevertToStatsTick;
        _revertToStatsTimer.Start();
    }

    public void CancelUpload()
    {
        _revertToStatsTimer?.Stop();
        _revertToStatsTimer = null;
        Mode = SidebarFooterMode.Stats;
    }

    private void OnRevertToStatsTick(object? sender, EventArgs e)
    {
        _revertToStatsTimer?.Stop();
        _revertToStatsTimer = null;
        Application.Current?.Dispatcher.Invoke(() => Mode = SidebarFooterMode.Stats);
    }

    private static string TruncateFileName(string name, int maxLen)
    {
        if (string.IsNullOrEmpty(name)) return "…";
        return name.Length <= maxLen ? name : name[..(maxLen - 1)] + "…";
    }

    private static string FilesNominative(int count) =>
        Pluralize(count, "файл", "файла", "файлов");

    private static string FilesGenitive(int count) =>
        count % 10 == 1 && count % 100 != 11 ? "файла" : "файлов";

    private static string Pluralize(int count, string form1, string form2, string form5)
    {
        var lastTwo = count % 100;
        var lastOne = count % 10;
        if (lastTwo >= 11 && lastTwo <= 19) return form5;
        if (lastOne == 1) return form1;
        if (lastOne >= 2 && lastOne <= 4) return form2;
        return form5;
    }
}
