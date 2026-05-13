using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Services;
using MPMS.Infrastructure;
namespace MPMS.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly IApiService _api;
    private readonly ISyncService _sync;
    private readonly IServiceProvider _sp;
    private readonly DispatcherTimer _onlineTimer;

    [ObservableProperty] private string _currentPage = "Home";
    [ObservableProperty] private bool _isSidebarExpanded = true;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _lastSyncText = "Ещё не синхронизировано";
    [ObservableProperty] private int _syncProjectCount;
    [ObservableProperty] private int _syncTaskCount;
    [ObservableProperty] private int _syncStageCount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ViewModelBase? _currentPageViewModel;
    [ObservableProperty] private string? _userAvatarPath;
    [ObservableProperty] private byte[]? _userAvatarData;

    private readonly System.Collections.Generic.Stack<string> _navigationHistory = new();

    public bool CanGoBack => _navigationHistory.Count > 0;

    public string SwitchAccountTooltip => "Сменить аккаунт";

    public string UserName => _auth.UserName ?? "—";
    public string UserRole => _auth.UserRole ?? "—";
    public string UserRoleDisplay => _auth.UserRole switch
    {
        "Administrator" or "Admin" => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager" => "Менеджер",
        "Foreman" => "Прораб",
        "Worker" => "Работник",
        { } r => r,
        _ => "—"
    };
    public string UserInitials => _auth.UserName is { Length: > 0 } name
        ? string.Concat(name.Split(' ').Take(2).Select(w => w[0]))
        : "?";

    public bool IsProjectsVisible =>
        !string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

    public bool IsAdminPanelVisible =>
        string.Equals(_auth.UserRole, "Administrator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Admin", StringComparison.OrdinalIgnoreCase);

    public MainViewModel(IAuthService auth, IApiService api, ISyncService sync, IServiceProvider sp)
    {
        _auth = auth;
        _api = api;
        _sync = sync;
        _sp = sp;

        // Загружаем сохранённое состояние боковой панели
        _isSidebarExpanded = LocalSettings.GetBool("SidebarExpanded", true);

        // Считываем реальное состояние подключения немедленно, чтобы значок был правильным
        // на самом первом кадре, прежде чем таймер сработает в первый раз.
        _isOnline = _sync.IsOnline;

        // DispatcherTimer работает в потоке UI — нет проблем с потоками.
        // Опрашивает SyncService.IsOnline (который читает IApiService.IsOnline, обновляемый после каждого HTTP-вызова).
        _onlineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _onlineTimer.Tick += OnOnlineTimerTick;
        _onlineTimer.Start();

        // Обновлять счётчики в попапе после каждой синхронизации
        _sync.OnlineStatusChanged += OnSyncStatusChanged;

        _ = RefreshAvatarAsync();
        Navigate("Home");
    }

    private void OnOnlineTimerTick(object? sender, EventArgs e)
    {
        var online = _sync.IsOnline;
        if (IsOnline != online)
        {
            IsOnline = online;
            StatusMessage = online ? string.Empty : "Офлайн режим — данные не синхронизируются";
        }
    }

    private void OnSyncStatusChanged(object? sender, bool online)
    {
        // Событие приходит из фонового потока — обновляем UI в главном потоке
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            IsOnline = online;
            await RefreshSyncCountsAsync();
        });
    }

    private async System.Threading.Tasks.Task RefreshSyncCountsAsync()
    {
        try
        {
            var dbFactory = _sp.GetService<IDbContextFactory<LocalDbContext>>();
            if (dbFactory is null) return;
            await using var db = await dbFactory.CreateDbContextAsync();
            SyncProjectCount = await db.Projects.CountAsync();
            SyncTaskCount = await db.Tasks.CountAsync();
            SyncStageCount = await db.TaskStages.CountAsync();
        }
        catch { }
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    partial void OnIsSidebarExpandedChanged(bool value)
    {
        LocalSettings.SetBool("SidebarExpanded", value);
    }

    [RelayCommand]
    public void Navigate(string page)
    {
        CurrentPage = page;
        ViewModelBase? vm = page switch
        {
            "Home" => _sp.GetRequiredService<HomeViewModel>(),
            "Projects" => _sp.GetRequiredService<ProjectsViewModel>(),
            "ClosedProjects" => _sp.GetRequiredService<ClosedProjectsViewModel>(),
            "Tasks" => _sp.GetRequiredService<TasksViewModel>(),
            "Files" => _sp.GetRequiredService<FilesPageViewModel>(),
            "Calendar" => _sp.GetRequiredService<CalendarViewModel>(),
            "Timeline" => _sp.GetRequiredService<TimelineViewModel>(),
            "Warehouse" => _sp.GetRequiredService<WarehouseViewModel>(),
            "Stages" => _sp.GetRequiredService<StagesViewModel>(),
            "Profile" => _sp.GetRequiredService<ProfileViewModel>(),
            "Admin" => _sp.GetRequiredService<AdminViewModel>(),
            "Settings" => null, 
            _ => null
        };

        if (vm is ILoadable loadable)
            _ = loadable.LoadAsync();

        CurrentPageViewModel = vm;
        _ = RefreshSyncCountsAsync();

        if (page == "Files")
        {
            MainWindow.Instance?.RestorePhotoViewerVisibility();
            MainWindow.Instance?.RestoreDocumentViewerVisibility();
        }
        else
        {
            MainWindow.Instance?.HidePhotoViewerTemporarily();
            MainWindow.Instance?.HideDocumentViewerTemporarily();
        }
    }

    private void PushNavigationHistory(string page)
    {
        if (CurrentPage != page && !string.IsNullOrEmpty(CurrentPage))
        {
            _navigationHistory.Push(CurrentPage);
            OnPropertyChanged(nameof(CanGoBack));
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_navigationHistory.Count > 0)
        {
            var previousPage = _navigationHistory.Pop();
            OnPropertyChanged(nameof(CanGoBack));
            NavigateInternal(previousPage, addToHistory: false);
        }
    }

    private void NavigateInternal(string page, bool addToHistory)
    {
        if (addToHistory)
            PushNavigationHistory(page);

        CurrentPage = page;
        ViewModelBase? vm = page switch
        {
            "Home" => _sp.GetRequiredService<HomeViewModel>(),
            "Projects" => _sp.GetRequiredService<ProjectsViewModel>(),
            "ClosedProjects" => _sp.GetRequiredService<ClosedProjectsViewModel>(),
            "Tasks" => _sp.GetRequiredService<TasksViewModel>(),
            "Files" => _sp.GetRequiredService<FilesPageViewModel>(),
            "Calendar" => _sp.GetRequiredService<CalendarViewModel>(),
            "Timeline" => _sp.GetRequiredService<TimelineViewModel>(),
            "Warehouse" => _sp.GetRequiredService<WarehouseViewModel>(),
            "Stages" => _sp.GetRequiredService<StagesViewModel>(),
            "Profile" => _sp.GetRequiredService<ProfileViewModel>(),
            "Admin" => _sp.GetRequiredService<AdminViewModel>(),
            "Settings" => null, 
            _ => null
        };

        if (vm is ILoadable loadable)
            _ = loadable.LoadAsync();

        CurrentPageViewModel = vm;

        // Обновляем счётчики синхронизации при навигации
        _ = RefreshSyncCountsAsync();

        // Показываем/скрываем PhotoViewerLayer и DocumentViewerLayer в зависимости от текущей страницы
        if (page == "Files")
        {
            MainWindow.Instance?.RestorePhotoViewerVisibility();
            MainWindow.Instance?.RestoreDocumentViewerVisibility();
        }
        else
        {
            MainWindow.Instance?.HidePhotoViewerTemporarily();
            MainWindow.Instance?.HideDocumentViewerTemporarily();
        }
    }

    public void NavigateToProject(Models.LocalProject project)
    {
        PushNavigationHistory("ProjectDetail");
        CurrentPage = "ProjectDetail";
        var vm = _sp.GetRequiredService<ProjectDetailViewModel>();
        vm.SetProject(project, () => GoBackCommand.Execute(null));
        _ = vm.LoadAsync();
        CurrentPageViewModel = vm;

        MainWindow.Instance?.HidePhotoViewerTemporarily();
        MainWindow.Instance?.HideDocumentViewerTemporarily();
    }

    /// <summary>Встроенный редактор этапа (полноэкранная страница, как карточка проекта).</summary>
    public void NavigateToStageEditor(StageEditViewModel vm)
    {
        PushNavigationHistory("StageEdit");
        CurrentPage = "StageEdit";
        CurrentPageViewModel = vm;
        _ = vm.LoadAsync();

        MainWindow.Instance?.HidePhotoViewerTemporarily();
        MainWindow.Instance?.HideDocumentViewerTemporarily();
    }

    [RelayCommand]
    private void NavigateToProjectCmd(Models.LocalProject project)
        => NavigateToProject(project);

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        IsSyncing = true;
        SetStatus("Синхронизация...");
        await _sync.SyncAsync();
        if (IsOnline)
        {
            var now = DateTime.Now;
            LastSyncText = $"Последняя синхронизация: {now:HH:mm}";
            SetStatus("Данные синхронизированы");
        }
        else
        {
            SetStatus("Нет соединения с сервером");
        }
        IsBusy = false;
        IsSyncing = false;
        await RefreshSyncCountsAsync();

        if (CurrentPageViewModel is ILoadable loadableVm)
        {
            loadableVm.Invalidate();
            _ = loadableVm.LoadAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshConnectionAsync()
    {
        await _api.ProbeAsync();
        var online = _sync.IsOnline;
        if (IsOnline != online)
        {
            IsOnline = online;
        }
        if (online)
            await _sync.SyncAsync();
    }

    public void RefreshUserInfo()
    {
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserRole));
        OnPropertyChanged(nameof(UserRoleDisplay));
        OnPropertyChanged(nameof(UserInitials));
        OnPropertyChanged(nameof(IsProjectsVisible));
        OnPropertyChanged(nameof(IsAdminPanelVisible));
        _ = RefreshAvatarAsync();
    }

    /// <summary>Вызывает RefreshUserInfo и затем навигацию на Home (для инициализации).</summary>
    public void RefreshUserInfoAndNavigateHome()
    {
        RefreshUserInfo();
        Navigate("Home");
    }

    public async Task RefreshAvatarAsync()
    {
        if (_auth.UserId is not { } uid) { UserAvatarPath = null; UserAvatarData = null; return; }
        try
        {
            var dbFactory = _sp.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FindAsync(uid);
            UserAvatarPath = user?.AvatarPath;
            UserAvatarData = user?.AvatarData;
        }
        catch { UserAvatarPath = null; UserAvatarData = null; }
    }

    [RelayCommand]
    private async Task SwitchAccount()
    {
        await LogLogoutAsync();
        _auth.Logout();
        App.NavigateToLogin();
    }

    private async Task LogLogoutAsync()
    {
        try
        {
            await using var db = await _sp.GetRequiredService<IDbContextFactory<Data.LocalDbContext>>().CreateDbContextAsync();
            var name = _auth.UserName ?? "?";
            var initials = Services.AvatarHelper.GetInitials(name);
            var color = Services.AvatarHelper.GetColorForName(name);
            var log = new Models.LocalActivityLog
            {
                UserId = _auth.UserId,
                ActorRole = _auth.UserRole,
                UserName = name,
                UserInitials = initials,
                UserColor = color,
                ActionType = Models.ActivityActionKind.Logout,
                ActionText = "Выход из системы",
                EntityType = "User",
                EntityId = _auth.UserId ?? Guid.Empty,
                CreatedAt = DateTime.UtcNow
            };
            db.ActivityLogs.Add(log);
            await db.SaveChangesAsync();
            await _sync.QueueLocalActivityLogAsync(log);
        }
        catch { /* некритичная ошибка */ }
    }
}

