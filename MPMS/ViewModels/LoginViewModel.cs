using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private ObservableCollection<RecentAccount> _recentAccounts = new();
    [ObservableProperty] private bool _hasRecentAccounts;

    public LoginViewModel(IApiService api, IAuthService auth, IDbContextFactory<LocalDbContext> dbFactory)
    {
        _api = api;
        _auth = auth;
        _dbFactory = dbFactory;
        _ = LoadRecentAccountsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ClearMessages();
        IsBusy = true;

        try
        {
            var result = await _api.LoginAsync(Username.Trim(), Password);

            if (result.Success)
            {
                var (allowed, blockMessage) = await _auth.CanUserLoginAsync(result.Response!.UserId);
                if (!allowed)
                {
                    SetError(blockMessage ?? "Вход запрещён.");
                    return;
                }
                await _auth.SetSessionAsync(result.Response!, Password);
                OpenMainAndClose();
                return;
            }

            // ── API недоступен → пробуем офлайн-вход ─────────────────────
            if (!_api.IsOnline)
            {
                var (offlineResponse, blockMessage) = await _auth.TryOfflineLoginAsync(Username.Trim(), Password);
                if (offlineResponse is not null)
                {
                    await _auth.SetSessionAsync(offlineResponse, Password);
                    OpenMainAndClose();
                    return;
                }
                if (blockMessage is not null)
                {
                    SetError(blockMessage);
                    return;
                }

                // Distinguish between "no local cache" and "wrong password"
                var hasCache = await _auth.HasLocalCacheAsync(Username.Trim());
                if (!hasCache)
                    SetError("Сервер недоступен. Для первого входа необходимо подключение к серверу. После успешного входа онлайн будет доступен офлайн-режим.");
                else
                    SetError("Сервер недоступен. Неверный пароль для офлайн-входа.");
                return;
            }

            SetError(result.Error ?? "Неизвестная ошибка.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAccount(RecentAccount account)
    {
        Username = account.Username;
        PasswordFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? PasswordFocusRequested;

    private bool CanLogin() => !string.IsNullOrWhiteSpace(Username)
                             && !string.IsNullOrWhiteSpace(Password);

    private void OpenMainAndClose()
    {
        _ = LogLoginAsync();
        App.OpenMainWindow();
        foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
        {
            if (w is Views.LoginWindow) { w.Close(); break; }
        }
    }

    private async Task LogLoginAsync()
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var name     = _auth.UserName ?? "?";
            var initials = Services.AvatarHelper.GetInitials(name);
            var color    = Services.AvatarHelper.GetColorForName(name);
            db.ActivityLogs.Add(new LocalActivityLog
            {
                UserId       = _auth.UserId,
                ActorRole    = _auth.UserRole,
                UserName     = name,
                UserInitials = initials,
                UserColor    = color,
                ActionType   = ActivityActionKind.Login,
                ActionText   = $"Вход в систему",
                EntityType   = "User",
                EntityId     = _auth.UserId ?? Guid.Empty,
                CreatedAt    = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch { /* non-critical */ }
    }

    private async Task LoadRecentAccountsAsync()
    {
        var accounts = await _auth.GetRecentAccountsAsync();
        RecentAccounts = new ObservableCollection<RecentAccount>(accounts);
        HasRecentAccounts = RecentAccounts.Count > 0;
    }

    partial void OnUsernameChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
}
