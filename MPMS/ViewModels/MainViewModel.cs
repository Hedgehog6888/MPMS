using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ViewModelBase? _currentPageViewModel;

    public string SwitchAccountTooltip => "Сменить аккаунт";

    public string UserName => _auth.UserName ?? "—";
    public string UserRole => _auth.UserRole ?? "—";
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

        Navigate(IsProjectsVisible ? "Projects" : "Tasks");
    }

    private void OnOnlineTimerTick(object? sender, EventArgs e)
    {
        var online = _sync.IsOnline;
        if (IsOnline == online) return;

        IsOnline = online;
        StatusMessage = online ? string.Empty : "Офлайн режим — данные не синхронизируются";
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
            _           => null
        };

        if (vm is ILoadable loadable)
            _ = loadable.LoadAsync();

        CurrentPageViewModel = vm;
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
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        SetStatus("Синхронизация...");
        await _sync.SyncAsync();
        SetStatus(IsOnline ? "Данные синхронизированы" : "Нет соединения с сервером");
        IsBusy = false;
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
        OnPropertyChanged(nameof(UserInitials));
        OnPropertyChanged(nameof(IsProjectsVisible));
        // Workers go to Tasks page by default
        Navigate(IsProjectsVisible ? "Projects" : "Tasks");
    }

    [RelayCommand]
    private void SwitchAccount()
    {
        _auth.Logout();
        App.NavigateToLogin();
    }
}

