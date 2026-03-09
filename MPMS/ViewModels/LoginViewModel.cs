using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private ObservableCollection<RecentAccount> _recentAccounts = new();
    [ObservableProperty] private bool _hasRecentAccounts;

    public LoginViewModel(IApiService api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
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

            if (!result.Success)
            {
                SetError(result.Error ?? "Неизвестная ошибка.");
                return;
            }

            _auth.SetSession(result.Response!);
            App.OpenMainWindow();

            foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
            {
                if (w is Views.LoginWindow) { w.Close(); break; }
            }
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
        // Signal the view to focus the password field
        PasswordFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? PasswordFocusRequested;

    private bool CanLogin() => !string.IsNullOrWhiteSpace(Username)
                             && !string.IsNullOrWhiteSpace(Password);

    private async Task LoadRecentAccountsAsync()
    {
        var accounts = await _auth.GetRecentAccountsAsync();
        RecentAccounts = new ObservableCollection<RecentAccount>(accounts);
        HasRecentAccounts = RecentAccounts.Count > 0;
    }

    partial void OnUsernameChanged(string value)  => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value)  => LoginCommand.NotifyCanExecuteChanged();
}
