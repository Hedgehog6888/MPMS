using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class StagesPage : UserControl
{
    public StagesPage()
    {
        InitializeComponent();
    }

    private StagesViewModel? VM => DataContext as StagesViewModel;

    private async void CreateStage_Click(object sender, RoutedEventArgs e)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Tasks.AnyAsync())
        {
            MessageBox.Show(
                "Сначала создайте хотя бы одну задачу.",
                "Нет задач", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var overlay = new CreateStageOverlay();
        var vm = VM;
        overlay.SetCreateModeFromStagesPage(async () =>
        {
            if (vm is not null) await vm.LoadAsync();
        });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        var result = MessageBox.Show(
            $"Удалить этап «{item.Stage.Name}»?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            await VM.DeleteStageCommand.ExecuteAsync(item);
    }

    private async void StartStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((item, StageStatus.InProgress));
    }

    private async void CompleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((item, StageStatus.Completed));
    }

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        var task = VM.GetTaskForStageAsync(item.TaskId).GetAwaiter().GetResult();
        if (task is null) return;
        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(item.Stage, task, async () => { if (VM is not null) await VM.LoadAsync(); });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void Stage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not StageItem item || VM is null) return;
        var task = await VM.GetTaskForStageAsync(item.TaskId);
        if (task is null) return;

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(task);

        var stageOverlay = new StageDetailOverlay();
        stageOverlay.SetStage(item, task, () => { _ = VM.LoadAsync(); });

        MainWindow.Instance?.ShowDrawer(taskPanel, stageOverlay, 850);
    }
}
