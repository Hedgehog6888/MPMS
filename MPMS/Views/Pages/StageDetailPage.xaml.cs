using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class StageDetailPage
{
    public StageDetailPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (FindName("DueDatePicker") is DatePicker dp)
                DueDatePickerRestrictions.AttachNoPastSelectableBlackout(dp);
            if (DataContext is StageDetailViewModel vm)
            {
                vm.OpenEditorRequested -= OnOpenEditorRequested;
                vm.OpenEditorRequested += OnOpenEditorRequested;
                
                // Set DataContext for the new panels
                StageManagementPanel.DataContext = DataContext;
                StageQuickActionsPanel.DataContext = DataContext;
                
                _ = Dispatcher.InvokeAsync(UpdatePanels, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is StageDetailViewModel vm)
                vm.OpenEditorRequested -= OnOpenEditorRequested;
        };
    }

    private void OnOpenEditorRequested(LocalTaskStage stage, LocalTask task)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(
            stage,
            task,
            onSaved: async () =>
            {
                await vm.LoadAsync();
                vm.SetViewMode(stage, task, () => vm.GoBackCommand.Execute(null));
            },
            onAfterSave: () => MainWindow.Instance?.HideDrawer());
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
    }

    private void StageTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.ActiveTab = tag;
    }

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;
        if (DataContext is StageDetailViewModel vm)
            vm.SelectedProjectId = row.Id;
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;
        if (DataContext is StageDetailViewModel vm)
            vm.SelectedTaskId = row.Id;
    }

    private void WorkerRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneePickerItem item) return;
        if (DataContext is StageDetailViewModel vm)
            vm.ToggleAssigneeCommand.Execute(item);
    }

    private void WorkerPeek_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneePickerItem item) return;
        if (DataContext is not StageDetailViewModel vm) return;
        if (vm.PeekProjectId is not Guid projectId) return;
        MainWindow.Instance?.TryOpenUserPeek(item.UserId, projectId);
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StageDetailViewModel vm)
            vm.FilesControlVM.UploadFileCommand.Execute(null);
    }

    private void UpdatePanels()
    {
        StageManagementPanel?.UpdateButtons();
        StageQuickActionsPanel?.UpdateButtons();
    }
}
