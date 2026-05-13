using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class StageEditPage
{
    public StageEditPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (FindName("DueDatePicker") is DatePicker dp)
                DueDatePickerRestrictions.AttachNoPastSelectableBlackout(dp);
        };
    }

    private void StageTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (DataContext is StageEditViewModel vm)
            vm.ActiveTab = tag;
    }

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;
        if (DataContext is StageEditViewModel vm)
            vm.SelectedProjectId = row.Id;
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;
        if (DataContext is StageEditViewModel vm)
            vm.SelectedTaskId = row.Id;
    }

    private void WorkerRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneePickerItem item) return;
        if (DataContext is StageEditViewModel vm)
            vm.ToggleAssigneeCommand.Execute(item);
    }

    private void WorkerPeek_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneePickerItem item) return;
        if (DataContext is not StageEditViewModel vm) return;
        if (vm.PeekProjectId is not Guid projectId) return;
        MainWindow.Instance?.TryOpenUserPeek(item.UserId, projectId);
    }
}
