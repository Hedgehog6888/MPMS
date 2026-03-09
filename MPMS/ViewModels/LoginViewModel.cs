using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IApiService _api;
    private readonly IAuthService _auth;

    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    public LoginViewModel(IApiService api, IAuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ClearMessages();
        IsBusy = true;

        try
        {
            var response = await _api.LoginAsync(Email.Trim(), Password);

            if (response is null)
            {
                SetError("Неверный email или пароль. Проверьте данные или соединение с сервером.");
                return;
            }

            _auth.SetSession(response);
            App.OpenMainWindow();

            // Close login window
            foreach (System.Windows.Window w in System.Windows.Application.Current.Windows)
            {
                if (w is Views.LoginWindow)
                {
                    w.Close();
                    break;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin() => !string.IsNullOrWhiteSpace(Email)
                             && !string.IsNullOrWhiteSpace(Password);

    partial void OnEmailChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
}
