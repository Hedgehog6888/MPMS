using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MPMS.ViewModels;

namespace MPMS.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    private static readonly SolidColorBrush FocusBrush  = new(Color.FromRgb(0x1B, 0x6E, 0xC2));
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0xE2, 0xE5, 0xED));

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.PasswordFocusRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => PwdBox.Focus());
    }

    // ── Password binding ──────────────────────────────────────────────────────
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.Password = PwdBox.Password;

    // ── Focus effects on input containers ────────────────────────────────────
    private void UsernameBox_GotFocus(object sender, RoutedEventArgs e)
        => UsernameContainer.BorderBrush = FocusBrush;

    private void UsernameBox_LostFocus(object sender, RoutedEventArgs e)
        => UsernameContainer.BorderBrush = NormalBrush;

    private void PwdBox_GotFocus(object sender, RoutedEventArgs e)
        => PasswordContainer.BorderBrush = FocusBrush;

    private void PwdBox_LostFocus(object sender, RoutedEventArgs e)
        => PasswordContainer.BorderBrush = NormalBrush;

    // ── Window drag ───────────────────────────────────────────────────────────
    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    // ── Close ─────────────────────────────────────────────────────────────────
    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}
