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
            oldVm.PropertyChanged -= Vm_PropertyChanged;
            oldVm.OpenCreateFormRequested -= OnOpenCreateForm;
            oldVm.OpenEditFormRequested -= OnOpenEditForm;
            oldVm.OpenUserInfoRequested -= OnOpenUserInfo;
            oldVm.OpenActivityDetailRequested -= OnOpenActivityDetail;
        }

        if (e.NewValue is not AdminViewModel vm) return;
        _vm = vm;

        vm.PropertyChanged += Vm_PropertyChanged;
        ApplyMainTab(vm.CurrentTab);
        vm.OpenCreateFormRequested += OnOpenCreateForm;
        vm.OpenEditFormRequested += OnOpenEditForm;
        vm.OpenUserInfoRequested += OnOpenUserInfo;
        vm.OpenActivityDetailRequested += OnOpenActivityDetail;
    }

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

    private void OnOpenActivityDetail(MPMS.Models.LocalActivityLog log)
    {
        var overlay = new AdminActivityDetailOverlay(log);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdminViewModel.CurrentTab) && _vm is not null)
            ApplyMainTab(_vm.CurrentTab);
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        var tag = rb.Tag?.ToString() ?? "Users";
        if (_vm is not null)
            _vm.CurrentTab = tag;

        ApplyMainTab(tag);

        if (tag == "Archive" && _vm is not null)
            _ = _vm.RefreshArchiveAsync();
    }

    private void ApplyMainTab(string tag)
    {
        UsersPanel.Visibility = tag == "Users" ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility = tag == "Archive" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPanel.Visibility = tag == "History" ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = tag == "Activity" ? Visibility.Visible : Visibility.Collapsed;

        TabUsers.IsChecked = tag == "Users";
        TabArchive.IsChecked = tag == "Archive";
        TabHistory.IsChecked = tag == "History";
        TabActivity.IsChecked = tag == "Activity";
    }
}
