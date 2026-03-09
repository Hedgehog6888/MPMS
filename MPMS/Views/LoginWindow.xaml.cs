using System.Windows;
using System.Windows.Input;
using MPMS.ViewModels;

namespace MPMS.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // When account card selected — focus password field
        vm.PasswordFocusRequested += (_, _) =>
        {
            Dispatcher.BeginInvoke(() => PwdBox.Focus());
        };
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.Password = PwdBox.Password;

    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}
