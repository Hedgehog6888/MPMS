using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class AdminPage : UserControl
{
    private AdminViewModel? _vm;

    public AdminPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not AdminViewModel vm) return;
        _vm = vm;

        vm.OpenCreateFormRequested += OnOpenCreateForm;
        vm.OpenEditFormRequested   += OnOpenEditForm;
        vm.OpenUserInfoRequested   += OnOpenUserInfo;

        _ = vm.LoadAsync();
    }

    // ── Drawer openers ────────────────────────────────────────────────────

    private void OnOpenCreateForm()
    {
        var overlay = new AdminUserFormOverlay();
        overlay.SetCreateMode(_vm!);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void OnOpenEditForm(AdminUserRow row)
    {
        var overlay = new AdminUserFormOverlay();
        overlay.SetEditMode(_vm!, row);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void OnOpenUserInfo(AdminUserRow row)
    {
        var overlay = new AdminUserInfoOverlay(row, _vm!);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    // ── Button clicks ─────────────────────────────────────────────────────

    private void CreateUser_Click(object sender, RoutedEventArgs e)
        => _vm?.OpenCreateFormCommand.Execute(null);

    private void UserRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AdminUserRow row)
            _vm?.ViewUserInfoCommand.Execute(row);
    }

    private void ArchiveRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ArchiveRow row)
        {
            var overlay = new ArchiveItemInfoOverlay(row, _vm!);
            MainWindow.Instance?.ShowDrawer(overlay);
        }
    }

    // ── Tab switching ─────────────────────────────────────────────────────

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var tag = rb.Tag?.ToString() ?? "Users";

        UsersPanel.Visibility    = tag == "Users"    ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility  = tag == "Archive"  ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility  = tag == "History"  ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = tag == "Activity" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ArchiveTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var tag = rb.Tag?.ToString() ?? "Projects";

        ArchProjPanel.Visibility  = tag == "Projects" ? Visibility.Visible : Visibility.Collapsed;
        ArchTaskPanel.Visibility  = tag == "Tasks"    ? Visibility.Visible : Visibility.Collapsed;
        ArchStagePanel.Visibility = tag == "Stages"   ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Overlay backdrop clicks ───────────────────────────────────────────

    private void BlockOverlayBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _vm?.CancelBlockOverlayCommand.Execute(null);

    private void UnblockOverlayBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _vm?.CancelUnblockOverlayCommand.Execute(null);

    private void ConfirmOverlayBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => _vm?.CancelConfirmCommand.Execute(null);

    // ── Search box focus ──────────────────────────────────────────────────

    private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x11, 0x11, 0x11));
    private static readonly SolidColorBrush ClearBrush  = new(Colors.Transparent);

    private void UserSearch_GotFocus(object s, RoutedEventArgs e)      => UserSearchBorder.BorderBrush = FocusBrush;
    private void UserSearch_LostFocus(object s, RoutedEventArgs e)     => UserSearchBorder.BorderBrush = ClearBrush;

    private void ArchiveSearch_GotFocus(object s, RoutedEventArgs e)   => ArchiveSearchBorder.BorderBrush = FocusBrush;
    private void ArchiveSearch_LostFocus(object s, RoutedEventArgs e)  => ArchiveSearchBorder.BorderBrush = ClearBrush;

    private void HistorySearch_GotFocus(object s, RoutedEventArgs e)   => HistorySearchBorder.BorderBrush = FocusBrush;
    private void HistorySearch_LostFocus(object s, RoutedEventArgs e)  => HistorySearchBorder.BorderBrush = ClearBrush;

    private void ActivitySearch_GotFocus(object s, RoutedEventArgs e)  => ActivitySearchBorder.BorderBrush = FocusBrush;
    private void ActivitySearch_LostFocus(object s, RoutedEventArgs e) => ActivitySearchBorder.BorderBrush = ClearBrush;

    // ── Search clear buttons ──────────────────────────────────────────────

    private void ClearUserSearch_Click(object s, RoutedEventArgs e)
    { if (_vm is not null) _vm.UserSearchText = string.Empty; }

    private void ClearArchiveSearch_Click(object s, RoutedEventArgs e)
    { if (_vm is not null) _vm.ArchiveSearchText = string.Empty; }

    private void ClearHistorySearch_Click(object s, RoutedEventArgs e)
    { if (_vm is not null) _vm.HistorySearchText = string.Empty; }

    private void ClearActivitySearch_Click(object s, RoutedEventArgs e)
    { if (_vm is not null) _vm.ActivitySearchText = string.Empty; }
}
