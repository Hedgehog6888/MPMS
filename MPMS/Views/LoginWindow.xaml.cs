using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MPMS.ViewModels;

namespace MPMS.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    private static readonly SolidColorBrush FocusBrush  = new(Color.FromRgb(0x6B, 0x77, 0x8C));
    private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(0xDF, 0xE1, 0xE6));

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.PasswordFocusRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => PwdBox.Focus());
    }

    // ── Password binding / show-hide toggle ──────────────────────────────────
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _vm.Password = PwdBox.Password;
        PwdVisibleBox.Text = PwdBox.Password;
    }

    private bool _pwdVisible;

    private void EyeIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pwdVisible = !_pwdVisible;

        if (_pwdVisible)
        {
            // Sync PasswordBox → TextBox before showing
            PwdVisibleBox.Text       = PwdBox.Password;
            PwdBox.Visibility        = Visibility.Collapsed;
            PwdVisibleBox.Visibility = Visibility.Visible;
            EyeOnIcon.Visibility     = Visibility.Visible;
            EyeOffIcon.Visibility    = Visibility.Collapsed;
            PwdVisibleBox.Focus();
            PwdVisibleBox.CaretIndex = PwdVisibleBox.Text.Length;
        }
        else
        {
            // Sync TextBox → PasswordBox before hiding
            var text = PwdVisibleBox.Text;
            PwdBox.Visibility        = Visibility.Visible;
            PwdVisibleBox.Visibility = Visibility.Collapsed;
            EyeOnIcon.Visibility     = Visibility.Collapsed;
            EyeOffIcon.Visibility    = Visibility.Visible;
            PwdBox.Password          = text;
            _vm.Password             = text;
            PwdBox.Focus();
            // Run before next render so user doesn't see caret jump from start to end.
            Dispatcher.Invoke(() => SetPasswordBoxCaretEnd(PwdBox),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void PwdVisibleBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => _vm.Password = PwdVisibleBox.Text;

    // ── Focus effects on input containers ────────────────────────────────────
    private void UsernameBox_GotFocus(object sender, RoutedEventArgs e)
        => UsernameContainer.BorderBrush = FocusBrush;

    private void UsernameBox_LostFocus(object sender, RoutedEventArgs e)
        => UsernameContainer.BorderBrush = NormalBrush;

    private void ApiUrlBox_GotFocus(object sender, RoutedEventArgs e)
        => ApiUrlContainer.BorderBrush = FocusBrush;

    private void ApiUrlBox_LostFocus(object sender, RoutedEventArgs e)
        => ApiUrlContainer.BorderBrush = NormalBrush;

    private void PwdBox_GotFocus(object sender, RoutedEventArgs e)
        => PasswordContainer.BorderBrush = FocusBrush;

    private void PwdBox_LostFocus(object sender, RoutedEventArgs e)
        => PasswordContainer.BorderBrush = NormalBrush;

    private static void SetPasswordBoxCaretEnd(PasswordBox box)
    {
        box.GetType()
            .GetMethod("Select", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(box, new object[] { box.Password.Length, 0 });
    }

    // ── Window drag ───────────────────────────────────────────────────────────
    private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    // ── Close ─────────────────────────────────────────────────────────────────
    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}
