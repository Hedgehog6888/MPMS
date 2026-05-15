using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
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

        Loaded += async (_, _) =>
        {
            if (FindName("DueDatePicker") is DatePicker dp)
                DueDatePickerRestrictions.AttachNoPastSelectableBlackout(dp);

            if (DataContext is StageDetailViewModel vm)
            {
                StageManagementPanel.DataContext = DataContext;

                _ = Dispatcher.InvokeAsync(UpdatePanels, System.Windows.Threading.DispatcherPriority.Loaded);
                await vm.LoadAsync();
            }
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
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var freshStage = await db.TaskStages.FindAsync(stage.Id);
                var freshTask = await db.Tasks.FindAsync(task.Id);

                if (freshStage is not null && freshTask is not null)
                {
                    vm.SetEditMode(freshStage, freshTask,
                        goBack: () => vm.GoBackCommand.Execute(null));
                    await vm.ReloadAllAsync();
                }
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

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm) return;

        if (vm.EditStage is null || vm.EditTask is null) return;

        OnOpenEditorRequested(vm.EditStage, vm.EditTask);
    }

    private void UpdatePanels()
    {
        StageManagementPanel?.UpdateButtons();
    }
}
