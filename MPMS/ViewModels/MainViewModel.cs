using System;
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
using MPMS.Views.Overlays;
namespace MPMS.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly IApiService _api;
    private readonly ISyncService _sync;
    private readonly IServiceProvider _sp;
    private readonly DispatcherTimer _onlineTimer;

    public SidebarFooterViewModel SidebarFooter { get; }

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

    private sealed record NavigationState(
        string Page,
        Guid? ProjectId = null,
        Guid? StageId = null,
        Guid? TaskId = null);

    private readonly System.Collections.Generic.Stack<NavigationState> _navigationHistory = new();

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

    public bool IsCatalogsVisible =>
        string.Equals(_auth.UserRole, "Administrator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);

    public bool CanCreateProject =>
        string.Equals(_auth.UserRole, "Administrator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);

    public bool CanCreateTask =>
        string.Equals(_auth.UserRole, "Administrator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);

    public bool CanCreateStage =>
        string.Equals(_auth.UserRole, "Administrator", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);

    public MainViewModel(IAuthService auth, IApiService api, ISyncService sync, IServiceProvider sp, SidebarFooterViewModel sidebarFooter)
    {
        _auth = auth;
        _api = api;
        _sync = sync;
        _sp = sp;
        SidebarFooter = sidebarFooter;

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
        _ = SidebarFooter.RefreshStatsAsync();
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
        if (CurrentPage != page && CurrentPageViewModel is FilesPageViewModel filesPageViewModel)
            filesPageViewModel.FilesControlVM.CancelSelectionModeCommand.Execute(null);

        if (CurrentPageViewModel is INavigable navigable)
            _ = navigable.OnNavigatingFromAsync();

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
            "Catalogs" => _sp.GetRequiredService<CatalogsViewModel>(),
            "Admin" => _sp.GetRequiredService<AdminViewModel>(),
            "Settings" => _sp.GetRequiredService<SettingsViewModel>(),
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

    private NavigationState CaptureCurrentState() => CurrentPage switch
    {
        "ProjectDetail" when CurrentPageViewModel is ProjectDetailViewModel { Project: { } project }
            => new NavigationState("ProjectDetail", ProjectId: project.Id),
        "StageDetail" when CurrentPageViewModel is StageDetailViewModel { EditStage: { } stage, EditTask: { } task }
            => new NavigationState("StageDetail", ProjectId: task.ProjectId, StageId: stage.Id, TaskId: task.Id),
        _ => new NavigationState(CurrentPage)
    };

    private void PushCurrentToHistory()
    {
        if (string.IsNullOrEmpty(CurrentPage)) return;
        _navigationHistory.Push(CaptureCurrentState());
        OnPropertyChanged(nameof(CanGoBack));
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_navigationHistory.Count == 0) return;
        var state = _navigationHistory.Pop();
        OnPropertyChanged(nameof(CanGoBack));
        _ = RestoreNavigationStateAsync(state);
    }

    private async Task RestoreNavigationStateAsync(NavigationState state)
    {
        if (CurrentPageViewModel is INavigable navigable)
            await navigable.OnNavigatingFromAsync();

        CurrentPage = state.Page;

        switch (state.Page)
        {
            case "ProjectDetail" when state.ProjectId is Guid projectId:
                await RestoreProjectDetailAsync(projectId);
                break;
            case "StageDetail" when state.StageId is Guid stageId && state.TaskId is Guid taskId:
                await RestoreStageDetailAsync(stageId, taskId);
                break;
            default:
                RestoreStandardPage(state.Page);
                break;
        }

        ApplyPageViewerVisibility(state.Page);
        _ = RefreshSyncCountsAsync();
    }

    private void RestoreStandardPage(string page)
    {
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
            "Catalogs" => _sp.GetRequiredService<CatalogsViewModel>(),
            "Admin" => _sp.GetRequiredService<AdminViewModel>(),
            "Settings" => _sp.GetRequiredService<SettingsViewModel>(),
            _ => null
        };

        if (vm is ILoadable loadable)
            _ = loadable.LoadAsync();

        CurrentPageViewModel = vm;
    }

    private async Task RestoreProjectDetailAsync(Guid projectId)
    {
        var dbFactory = _sp.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(projectId);
        if (project is null)
        {
            RestoreStandardPage("Projects");
            return;
        }

        var vm = _sp.GetRequiredService<ProjectDetailViewModel>();
        vm.SetProject(project, () => GoBackCommand.Execute(null));
        CurrentPageViewModel = vm;
        await vm.LoadAsync();
    }

    private async Task RestoreStageDetailAsync(Guid stageId, Guid taskId)
    {
        var dbFactory = _sp.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(stageId);
        var task = await db.Tasks.FindAsync(taskId);
        if (stage is null || task is null)
        {
            if (task?.ProjectId is Guid projectId)
                await RestoreProjectDetailAsync(projectId);
            else
                RestoreStandardPage("Stages");
            return;
        }

        var vm = _sp.GetRequiredService<StageDetailViewModel>();
        vm.SetEditMode(stage, task, () => GoBackCommand.Execute(null));
        CurrentPageViewModel = vm;
        await vm.LoadAsync();
    }

    private static void ApplyPageViewerVisibility(string page)
    {
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
        PushCurrentToHistory();
        CurrentPage = "ProjectDetail";
        var vm = _sp.GetRequiredService<ProjectDetailViewModel>();
        vm.SetProject(project, () => GoBackCommand.Execute(null));
        _ = vm.LoadAsync();
        CurrentPageViewModel = vm;

        MainWindow.Instance?.HidePhotoViewerTemporarily();
        MainWindow.Instance?.HideDocumentViewerTemporarily();
    }

    /// <summary>Встроенный редактор этапа (полноэкранная страница, как карточка проекта).</summary>
    public void NavigateToStageEditor(StageDetailViewModel vm)
    {
        PushCurrentToHistory();
        CurrentPage = "StageDetail";
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
        await SidebarFooter.RefreshStatsAsync();
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
        OnPropertyChanged(nameof(IsCatalogsVisible));
        OnPropertyChanged(nameof(IsAdminPanelVisible));
        OnPropertyChanged(nameof(CanCreateProject));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanCreateStage));
        _ = RefreshAvatarAsync();

        // Обновляем роль в FilesControlViewModel если текущая страница - Files
        if (CurrentPage == "Files" && CurrentPageViewModel is FilesPageViewModel filesPageVm)
        {
            filesPageVm.FilesControlVM.RefreshUserInfo();
        }
    }

    /// <summary>Вызывает RefreshUserInfo и затем навигацию на Home (для инициализации).</summary>
    public void RefreshUserInfoAndNavigateHome()
    {
        RefreshUserInfo();
        _ = SidebarFooter.RefreshStatsAsync();
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
    private void NavigateToCreateProject()
    {
        Navigate("Projects");
        // Открываем оверлей после загрузки страницы
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var projectsVm = _sp.GetRequiredService<ProjectsViewModel>();
            var overlay = new CreateProjectOverlay();
            overlay.SetCreateMode(projectsVm);
            MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    [RelayCommand]
    private void NavigateToCreateTask()
    {
        Navigate("Tasks");
        // Открываем оверлей после загрузки страницы
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var tasksVm = _sp.GetRequiredService<TasksViewModel>();
            var overlay = new CreateTaskOverlay();
            overlay.SetCreateMode(tasksVm,
                onSaved: async () => { await tasksVm.LoadAsync(); });
            MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    [RelayCommand]
    private void NavigateToCreateStage()
    {
        Navigate("Stages");
        // Открываем оверлей после загрузки страницы
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var stagesVm = _sp.GetRequiredService<StagesViewModel>();
            var overlay = new CreateStageOverlay();
            overlay.SetCreateMode(
                onSaved: async () => { await stagesVm.LoadAsync(); },
                onAfterSave: () => MainWindow.Instance?.HideDrawer());
            MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
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

