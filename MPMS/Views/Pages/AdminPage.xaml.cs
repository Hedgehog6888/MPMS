using System.Windows;
using System.Windows.Controls;
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
        if (e.OldValue is AdminViewModel oldVm)
        {
            oldVm.OpenCreateFormRequested -= OnOpenCreateForm;
            oldVm.OpenEditFormRequested   -= OnOpenEditForm;
            oldVm.OpenUserInfoRequested   -= OnOpenUserInfo;
        }

        if (e.NewValue is not AdminViewModel vm) return;
        _vm = vm;

        vm.OpenCreateFormRequested += OnOpenCreateForm;
        vm.OpenEditFormRequested   += OnOpenEditForm;
        vm.OpenUserInfoRequested   += OnOpenUserInfo;
    }

    // ── Drawer openers ────────────────────────────────────────────────────

    private void OnOpenCreateForm()
    {
        var overlay = new AdminUserFormOverlay();
        overlay.SetCreateMode(_vm!);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    private void OnOpenEditForm(AdminUserRow row)
    {
        var overlay = new AdminUserFormOverlay();
        overlay.SetEditMode(_vm!, row);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    private void OnOpenUserInfo(AdminUserRow row)
    {
        var overlay = new AdminUserInfoOverlay(row, _vm!);
        MainWindow.Instance?.ShowDrawer(overlay);
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

        if (tag == "Archive" && _vm is not null)
            _ = _vm.RefreshArchiveAsync();
    }
}
