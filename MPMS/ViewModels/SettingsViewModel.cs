using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Services;
using System.Windows;

namespace MPMS.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAuthService _auth;

    public SettingsViewModel(IAuthService auth)
    {
        _auth = auth;
        ApiServerUrl = _auth.ApiBaseUrl;
    }

    // Notifications
    [ObservableProperty] private bool _soundNotificationsEnabled = true;
    [ObservableProperty] private bool _popupNotificationsEnabled = true;
    [ObservableProperty] private bool _fileDownloadSoundEnabled = true;
    [ObservableProperty] private bool _syncErrorSoundEnabled = true;

    // Files
    [ObservableProperty] private string _defaultDownloadFolder = "C:\\Downloads";
    [ObservableProperty] private bool _autoOpenDocuments = false;

    // Reports
    [ObservableProperty] private string _ks2ReportSettings = "Настройки KS2";
    [ObservableProperty] private int _reportLanguageIndex = 0;
    [ObservableProperty] private string _watermarkText = "";

    // Sync
    [ObservableProperty] private string _apiServerUrl = "";
    [ObservableProperty] private bool _autoSyncOnStart = true;
    [ObservableProperty] private int _syncIntervalIndex = 1;
    [ObservableProperty] private bool _showSyncDetails = false;
    [ObservableProperty] private bool _autoReconnect = true;

    // Additional
    [ObservableProperty] private bool _energySavingMode = false;
    [ObservableProperty] private bool _minimizeToTray = false;
    [ObservableProperty] private bool _runOnStartup = false;
    [ObservableProperty] private bool _showInTaskbar = true;

    // Logging
    [ObservableProperty] private int _logLevelIndex = 1; // 0=Error, 1=Warning, 2=Info
    [ObservableProperty] private string _logFolder = "C:\\Logs\\MPMS";

    // Date/Time Format
    [ObservableProperty] private int _dateFormatIndex = 0; // 0=DD.MM.YYYY, 1=MM/DD/YYYY

    [RelayCommand]
    private void BrowseDownloadFolder()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите любой файл в папке",
            Filter = "Все файлы (*.*)|*.*"
        };

        if (!string.IsNullOrEmpty(DefaultDownloadFolder) && System.IO.Directory.Exists(DefaultDownloadFolder))
        {
            dialog.InitialDirectory = DefaultDownloadFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            var dir = System.IO.Path.GetDirectoryName(dialog.FileName);
            if (dir != null)
                DefaultDownloadFolder = dir;
        }
    }

    [RelayCommand]
    private void BrowseWatermarkImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите изображение для водяного знака",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            WatermarkText = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseLogFolder()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите любой файл в папке логов",
            Filter = "Все файлы (*.*)|*.*"
        };

        if (!string.IsNullOrEmpty(LogFolder) && System.IO.Directory.Exists(LogFolder))
        {
            dialog.InitialDirectory = LogFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            var dir = System.IO.Path.GetDirectoryName(dialog.FileName);
            if (dir != null)
                LogFolder = dir;
        }
    }

    [RelayCommand]
    private void ExportSettings()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Экспорт настроек",
            Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
            DefaultExt = "json",
            FileName = "mpms_settings.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var settings = new
                {
                    SoundNotificationsEnabled,
                    PopupNotificationsEnabled,
                    FileDownloadSoundEnabled,
                    SyncErrorSoundEnabled,
                    DefaultDownloadFolder,
                    AutoOpenDocuments,
                    ReportLanguageIndex,
                    WatermarkText,
                    AutoSyncOnStart,
                    SyncIntervalIndex,
                    ShowSyncDetails,
                    AutoReconnect,
                    EnergySavingMode,
                    MinimizeToTray,
                    RunOnStartup,
                    ShowInTaskbar,
                    LogLevelIndex,
                    LogFolder,
                    DateFormatIndex
                };

                var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(dialog.FileName, json);

                MessageBox.Show("Настройки успешно экспортированы", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при экспорте: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Импорт настроек",
            Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = System.IO.File.ReadAllText(dialog.FileName);
                var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

                if (settings != null)
                {
                    if (settings.TryGetValue("SoundNotificationsEnabled", out var v)) SoundNotificationsEnabled = Convert.ToBoolean(v);
                    if (settings.TryGetValue("PopupNotificationsEnabled", out v)) PopupNotificationsEnabled = Convert.ToBoolean(v);
                    if (settings.TryGetValue("FileDownloadSoundEnabled", out v)) FileDownloadSoundEnabled = Convert.ToBoolean(v);
                    if (settings.TryGetValue("SyncErrorSoundEnabled", out v)) SyncErrorSoundEnabled = Convert.ToBoolean(v);
                    if (settings.TryGetValue("DefaultDownloadFolder", out v)) DefaultDownloadFolder = v.ToString() ?? "";
                    if (settings.TryGetValue("AutoOpenDocuments", out v)) AutoOpenDocuments = Convert.ToBoolean(v);
                    if (settings.TryGetValue("ReportLanguageIndex", out v)) ReportLanguageIndex = Convert.ToInt32(v);
                    if (settings.TryGetValue("WatermarkText", out v)) WatermarkText = v.ToString() ?? "";
                    if (settings.TryGetValue("AutoSyncOnStart", out v)) AutoSyncOnStart = Convert.ToBoolean(v);
                    if (settings.TryGetValue("SyncIntervalIndex", out v)) SyncIntervalIndex = Convert.ToInt32(v);
                    if (settings.TryGetValue("ShowSyncDetails", out v)) ShowSyncDetails = Convert.ToBoolean(v);
                    if (settings.TryGetValue("AutoReconnect", out v)) AutoReconnect = Convert.ToBoolean(v);
                    if (settings.TryGetValue("EnergySavingMode", out v)) EnergySavingMode = Convert.ToBoolean(v);
                    if (settings.TryGetValue("MinimizeToTray", out v)) MinimizeToTray = Convert.ToBoolean(v);
                    if (settings.TryGetValue("RunOnStartup", out v)) RunOnStartup = Convert.ToBoolean(v);
                    if (settings.TryGetValue("ShowInTaskbar", out v)) ShowInTaskbar = Convert.ToBoolean(v);
                    if (settings.TryGetValue("LogLevelIndex", out v)) LogLevelIndex = Convert.ToInt32(v);
                    if (settings.TryGetValue("LogFolder", out v)) LogFolder = v.ToString() ?? "";
                    if (settings.TryGetValue("DateFormatIndex", out v)) DateFormatIndex = Convert.ToInt32(v);

                    MessageBox.Show("Настройки успешно импортированы", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при импорте: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
