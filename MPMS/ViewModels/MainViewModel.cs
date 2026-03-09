using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;

    [ObservableProperty] private string _currentPage = "Projects";
    [ObservableProperty] private bool _isSidebarExpanded = true;
    [ObservableProperty] private bool _isOnline = true;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private object? _pageContent;

    public string UserName => _auth.UserName ?? "—";
    public string UserRole => _auth.UserRole ?? "—";
    public string UserInitials => _auth.UserName is { Length: > 0 } name
        ? string.Concat(name.Split(' ').Take(2).Select(w => w[0]))
        : "?";

    public MainViewModel(IAuthService auth, ISyncService sync)
    {
        _auth = auth;
        _sync = sync;
        _sync.OnlineStatusChanged += (_, online) =>
        {
            IsOnline = online;
            StatusMessage = online ? string.Empty : "Офлайн режим";
        };
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPage = page;
        // In the next iteration each page will load its own ViewModel/View
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
    private void Logout()
    {
        _auth.Logout();
        App.NavigateToLogin();
    }
}
