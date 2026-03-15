using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Services;
using MPMS.Views.Dialogs;

namespace MPMS.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly IApiService _api;
    private readonly ISyncService _sync;
    private readonly IServiceProvider _sp;
    private readonly DispatcherTimer _onlineTimer;

    [ObservableProperty] private string _currentPage = "Projects";
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

    public string SwitchAccountTooltip => "Сменить аккаунт";

    public string UserName => _auth.UserName ?? "—";
    public string UserRole => _auth.UserRole ?? "—";
    public string UserRoleDisplay => _auth.UserRole switch
    {
        "Administrator" or "Admin"          => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager" => "Менеджер",
        "Foreman"                           => "Прораб",
        "Worker"                            => "Работник",
        { } r                               => r,
        _                                   => "—"
    };
    public string UserInitials => _auth.UserName is { Length: > 0 } name
        ? string.Concat(name.Split(' ').Take(2).Select(w => w[0]))
        : "?";

    public bool IsProjectsVisible =>
        !string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

    public MainViewModel(IAuthService auth, IApiService api, ISyncService sync, IServiceProvider sp)
    {
        _auth = auth;
        _api = api;
        _sync = sync;
        _sp = sp;

        // Read the real connectivity state immediately so the badge is correct
        // on the very first frame, before the timer fires for the first time.
        _isOnline = _sync.IsOnline;

        // DispatcherTimer runs on the UI thread — no threading issues.
        // Polls SyncService.IsOnline (which reads IApiService.IsOnline updated after every HTTP call).
        _onlineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _onlineTimer.Tick += OnOnlineTimerTick;
        _onlineTimer.Start();

        _ = RefreshAvatarAsync();
        Navigate(IsProjectsVisible ? "Projects" : "Tasks");
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

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPage = page;
        ViewModelBase? vm = page switch
        {
            "Projects"  => _sp.GetRequiredService<ProjectsViewModel>(),
            "Tasks" or "Kanban" or "Gantt" or "Calendar" or "Files" or "Journal"
                => _sp.GetRequiredService<TasksViewModel>(),
            "Materials" => _sp.GetRequiredService<MaterialsViewModel>(),
            "Stages"    => _sp.GetRequiredService<StagesViewModel>(),
            "Profile"   => _sp.GetRequiredService<ProfileViewModel>(),
            "Settings"  => null, // handled via overlay
            _           => null
        };

        if (vm is ILoadable loadable)
            _ = loadable.LoadAsync();

        CurrentPageViewModel = vm;

        // Refresh sync counts when navigating
        _ = RefreshSyncCountsAsync();
    }

    [RelayCommand]
    private void Create()
    {
        var window = Application.Current.MainWindow;
        
        var dialog = _sp.GetRequiredService<CreateProjectDialog>();
        dialog.Owner = window;
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var projectsVm = _sp.GetRequiredService<ProjectsViewModel>();
            _ = projectsVm.SaveNewProjectAsync(dialog.Result, Guid.NewGuid());
            Navigate("Projects");
        }
    }

    public void NavigateToProject(Models.LocalProject project)
    {
        CurrentPage = "ProjectDetail";
        var vm = _sp.GetRequiredService<ProjectDetailViewModel>();
        vm.SetProject(project, () => Navigate("Projects"));
        _ = vm.LoadAsync();
        CurrentPageViewModel = vm;
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
        var now = DateTime.Now;
        LastSyncText = $"Последняя синхронизация: {now:HH:mm}";
        SetStatus(IsOnline ? "Данные синхронизированы" : "Нет соединения с сервером");
        IsBusy = false;
        IsSyncing = false;
        await RefreshSyncCountsAsync();
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

    /// <summary>
    /// Called by App.OpenMainWindow() after a new login so the sidebar reflects the current user.
    /// </summary>
    public void RefreshUserInfo()
    {
        OnPropertyChanged(nameof(UserName));
        OnPropertyChanged(nameof(UserRole));
        OnPropertyChanged(nameof(UserRoleDisplay));
        OnPropertyChanged(nameof(UserInitials));
        OnPropertyChanged(nameof(IsProjectsVisible));
        _ = RefreshAvatarAsync();
        // Workers go to Tasks page by default
        Navigate(IsProjectsVisible ? "Projects" : "Tasks");
    }

    /// <summary>Reloads the avatar path from the local DB and notifies the UI.</summary>
    public async Task RefreshAvatarAsync()
    {
        if (_auth.UserId is not { } uid) { UserAvatarPath = null; return; }
        try
        {
            var dbFactory = _sp.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FindAsync(uid);
            UserAvatarPath = user?.AvatarPath;
        }
        catch { UserAvatarPath = null; }
    }

    [RelayCommand]
    private void SwitchAccount()
    {
        _auth.Logout();
        App.NavigateToLogin();
    }
}

