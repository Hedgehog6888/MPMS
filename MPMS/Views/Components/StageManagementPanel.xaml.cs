using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Components;

public partial class StageManagementPanel : UserControl
{
    public StageManagementPanel()
    {
        InitializeComponent();
    }

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm || vm.EditStage is null || vm.EditTask is null) return;
        var stage = vm.EditStage;
        var task = vm.EditTask;
        var goBack = vm.GoBackCommand;
        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(stage, task,
            onSaved: async () =>
            {
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var freshStage = await db.TaskStages.FindAsync(stage.Id);
                var freshTask = await db.Tasks.FindAsync(task.Id);
                if (freshStage is not null && freshTask is not null)
                {
                    vm.SetEditMode(freshStage, freshTask,
                        goBack: () => goBack.Execute(null));
                    await vm.ReloadAllAsync();
                }
                UpdateButtons();
            },
            onAfterSave: () => MainWindow.Instance?.HideDrawer());
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
    }

    private async void ChangeStatus_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        if (vm.EditStage is null) return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        if (owner is null) return;

        if (vm.StageStatus == StageStatus.Planned)
        {
            if (!StageStatusChangeDialog.Show(owner, vm.StageName, "Запланирован", "Выполняется",
                currentStatusColor: "#64748B", newStatusColor: "#3B82F6",
                currentStatusTextColor: "#FFFFFF", newStatusTextColor: "#FFFFFF"))
                return;
            await vm.StartStageCommand.ExecuteAsync(null);
        }
        else if (vm.StageStatus == StageStatus.InProgress)
        {
            if (!StageStatusChangeDialog.Show(owner, vm.StageName, "Выполняется", "Завершён",
                currentStatusColor: "#3B82F6", newStatusColor: "#10B981",
                currentStatusTextColor: "#FFFFFF", newStatusTextColor: "#FFFFFF"))
                return;
            await vm.CompleteStageCommand.ExecuteAsync(null);
        }
        else if (vm.StageStatus == StageStatus.Completed)
        {
            if (!StageStatusChangeDialog.Show(owner, vm.StageName, "Завершён", "Запланирован",
                currentStatusColor: "#10B981", newStatusColor: "#64748B",
                currentStatusTextColor: "#FFFFFF", newStatusTextColor: "#FFFFFF"))
                return;
            await vm.StartStageCommand.ExecuteAsync(null);
        }

        UpdateButtons();
    }

    private async void MarkStage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        if (!vm.IsStageMarkedForDeletion)
        {
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner is null || !ConfirmDeleteDialog.ShowMarkForDeletion(owner, "этап", vm.StageName))
                return;
        }
        
        await vm.MarkStageForDeletionCommand.ExecuteAsync(null);
        UpdateButtons();
    }

    public void UpdateButtons()
    {
        if (DataContext is not StageDetailViewModel vm) return;
        bool marked = vm.IsStageMarkedForDeletion;

        MarkStageBtn.ApplyTemplate();
        if (MarkStageBtn.Template?.FindName("MarkBtnText", MarkStageBtn) is System.Windows.Controls.TextBlock tb)
            tb.Text = marked ? "Снять пометку удаления" : "Пометить к удалению";

        EditStageBtn.IsEnabled = !marked;
        EditStageBtn.Opacity = marked ? 0.5 : 1.0;

        ChangeStatusBtn.IsEnabled = !marked;
        ChangeStatusBtn.Opacity = marked ? 0.5 : 1.0;
    }
}
