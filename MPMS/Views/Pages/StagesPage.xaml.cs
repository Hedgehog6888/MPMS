using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
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
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    private async void MarkStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        await VM.MarkStageForDeletionCommand.ExecuteAsync(item);
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        var owner = Window.GetWindow(this);
        if (MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(owner, "Этап", item.Stage.Name))
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

    private async void RevertStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((item, StageStatus.Planned));
    }

    private async void ReopenStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((item, StageStatus.InProgress));
    }

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StageItem item || VM is null) return;
        e.Handled = true;
        var task = VM.GetTaskForStageAsync(item.TaskId).GetAwaiter().GetResult();
        if (task is null) return;
        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(item.Stage, task, async () => { if (VM is not null) await VM.LoadAsync(); });
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    private static readonly SolidColorBrush _focusBrush = new(Colors.Black);
    private static readonly SolidColorBrush _normalBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush _focusBg = new(Colors.White);
    private static readonly SolidColorBrush _normalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));
    private static readonly System.Windows.Media.Effects.DropShadowEffect _focusShadow = new()
    {
        Color = Colors.Black, BlurRadius = 6, Opacity = 0.10, ShadowDepth = 0
    };

    private static Border? FindSearchBorder(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is Border b) return b;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && FindSearchBorder(tb) is { } border)
        {
            border.BorderBrush = _focusBrush;
            border.Background = _focusBg;
            border.Effect = _focusShadow;
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && FindSearchBorder(tb) is { } border)
        {
            border.BorderBrush = _normalBrush;
            border.Background = _normalBg;
            border.Effect = null;
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (VM is not null) VM.SearchText = string.Empty;
    }

    private void FilterBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FormComboHelpers.IsMouseWheelOverOpenComboBox(e))
            return;
        if (MainListScroll is null) return;
        var next = MainListScroll.VerticalOffset - e.Delta;
        next = Math.Max(0, Math.Min(next, MainListScroll.ScrollableHeight));
        MainListScroll.ScrollToVerticalOffset(next);
        e.Handled = true;
    }

    private async void Stage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not StageItem item || VM is null) return;
        var task = await VM.GetTaskForStageAsync(item.TaskId);
        if (task is null) return;

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(task);

        var stageOverlay = new StageDetailOverlay();
        var taskId = item.TaskId;
        stageOverlay.SetStage(item, task, () =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await VM.LoadAsync();
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var updatedTask = await db.Tasks.FindAsync(taskId);
                if (updatedTask != null)
                {
                    await ProgressCalculator.ApplyTaskMetricsForTaskAsync(db, updatedTask);
                    await Dispatcher.InvokeAsync(() => taskPanel.SetTask(updatedTask));
                }
            });
        });

        MainWindow.Instance?.ShowDrawer(taskPanel, stageOverlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
    }
}
