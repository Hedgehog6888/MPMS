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
    private readonly ISyncService _sync;

    [ObservableProperty] private string _apiBaseUrl = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private ObservableCollection<RecentAccount> _recentAccounts = new();
    [ObservableProperty] private bool _hasRecentAccounts;

    public LoginViewModel(IApiService api, IAuthService auth, IDbContextFactory<LocalDbContext> dbFactory,
        ISyncService sync)
    {
        _api = api;
        _auth = auth;
        _dbFactory = dbFactory;
        _sync = sync;
        _apiBaseUrl = auth.ApiBaseUrl;
        _ = LoadRecentAccountsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ClearMessages();
        IsBusy = true;

        try
        {
            await _auth.PersistApiBaseUrlForNextLoginAsync(ApiBaseUrl);
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
                await OpenMainAndCloseAsync();
                return;
            }

            // Онлайн не удался — всегда пробуем локальный кэш (офлайн-режим с теми же логином/паролем)
            var (offlineResponse, offlineBlock) = await _auth.TryOfflineLoginAsync(Username.Trim(), Password);
            if (offlineResponse is not null)
            {
                await _auth.SetSessionAsync(offlineResponse, Password);
                await OpenMainAndCloseAsync();
                return;
            }
            if (offlineBlock is not null)
            {
                SetError(offlineBlock);
                return;
            }

            // Нет локального входа: сообщаем в зависимости от того, ответил ли сервер
            if (!_api.IsOnline)
            {
                var hasCache = await _auth.HasLocalCacheAsync(Username.Trim());
                if (!hasCache)
                    SetError("Нет связи с сервером. Для первого входа нужен API. После успешного входа онлайн будет доступен офлайн-режим.");
                else
                    SetError("Нет связи с сервером или неверный пароль для офлайн-входа по сохранённому кэшу.");
            }
            else
                SetError(result.Error ?? "Не удалось войти.");
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

    private async Task OpenMainAndCloseAsync()
    {
        await LogLoginAsync();
        await App.OpenMainWindowAsync();
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
            var log = new LocalActivityLog
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
            };
            db.ActivityLogs.Add(log);
            await db.SaveChangesAsync();
            await _sync.QueueLocalActivityLogAsync(log);
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
