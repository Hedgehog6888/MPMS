using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class CalendarPage : UserControl
{
    private CalendarViewModel? _vm;

    public CalendarPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _vm = DataContext as CalendarViewModel;
        UpdateMaxChipsFromCalendarWidth();
    }

    private void CalendarCardBorder_Loaded(object sender, RoutedEventArgs e)
        => UpdateMaxChipsFromCalendarWidth();

    private void CalendarCardBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateMaxChipsFromCalendarWidth();

    /// <summary>
    /// Плашки по ширине колонки (ширина карточки календаря / 7).
    /// Окно MinWidth 1280 и сайдбар 220 дают ячейку ~147px — порог для трёх плашек выше этого.
    /// </summary>
    private static int MaxChipsFromCellWidth(double cellWidth)
    {
        if (double.IsNaN(cellWidth) || cellWidth <= 0) return 2;
        if (cellWidth >= 200) return 4;
        if (cellWidth >= 156) return 3;
        return 2;
    }

    private void UpdateMaxChipsFromCalendarWidth()
    {
        if (_vm is null) return;
        var w = CalendarCardBorder.ActualWidth;
        if (w <= 0 || double.IsNaN(w)) return;
        var cellW = w / 7.0;
        var max = MaxChipsFromCellWidth(cellW);
        if (_vm.MaxVisibleChipsPerDay != max)
            _vm.MaxVisibleChipsPerDay = max;
    }

    /// <summary>Click on a day cell → centered overlay with all tasks for that date.</summary>
    private void Cell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not CalendarCell cell) return;
        if (cell.IsEmpty) return;

        e.Handled = true;

        var overlay = new CalendarDayTasksOverlay();
        overlay.SetDay(cell.Date, cell.Tasks, cell.DayStages, OpenTaskDetail, OpenStageDetail);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 480);
    }

    /// <summary>Клик по плашке задачи/этапа в ячейке.</summary>
    private void Chip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not CalendarChipItem chip) return;
        e.Handled = true;

        if (chip.IsStage)
        {
            if (chip.Stage is null || chip.StageParentTask is null) return;
            OpenStageDetail(chip.Stage, chip.StageParentTask);
        }
        else if (chip.Task is not null)
            OpenTaskDetail(chip.Task);
    }

    private void OpenTaskDetail(LocalTask task)
        => _ = OpenTaskDetailAsync(task);

    private async Task OpenTaskDetailAsync(LocalTask task)
    {
        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        ProjectSummaryPanel? projectPanel = null;
        UIElement? leftPanel = null;
        var project = await tasksVm.GetProjectForTaskAsync(task.ProjectId);
        if (project is not null)
        {
            projectPanel = new ProjectSummaryPanel();
            projectPanel.SetProject(project);
            leftPanel = projectPanel;
        }

        var projectId = task.ProjectId;
        var detail = new TaskDetailOverlay();
        detail.SetTask(
            task,
            () =>
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await RefreshCalendarAsync();
                    var p = await tasksVm.GetProjectForTaskAsync(projectId);
                    if (p is not null && projectPanel is not null)
                        projectPanel.SetProject(p);
                });
            },
            TaskDetailOverlay.TaskDetailDrawerMode.WithProjectSummary);

        if (leftPanel is not null)
            MainWindow.Instance?.ShowDrawer(leftPanel, detail, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
        else
            MainWindow.Instance?.ShowDrawer(detail, MainWindow.TaskOrStageDetailDrawerWidth);
    }

    private void OpenStageDetail(LocalTaskStage stage, LocalTask parentTask)
    {
        var item = new StageItem
        {
            Stage       = stage,
            TaskId      = parentTask.Id,
            TaskName    = parentTask.Name,
            ProjectId   = parentTask.ProjectId,
            ProjectName = parentTask.ProjectName ?? "—"
        };

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(parentTask);

        var overlay = new StageDetailOverlay();
        var taskId = parentTask.Id;
        overlay.SetStage(item, parentTask, () =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await RefreshCalendarAsync();
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var updatedTask = await db.Tasks.FindAsync(taskId);
                if (updatedTask is not null)
                {
                    await ProgressCalculator.ApplyTaskMetricsForTaskAsync(db, updatedTask);
                    await Dispatcher.InvokeAsync(() => taskPanel.SetTask(updatedTask));
                }
            });
        });

        MainWindow.Instance?.ShowDrawer(taskPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
    }

    private async Task RefreshCalendarAsync()
    {
        if (_vm is not null)
            await _vm.LoadAsync();
    }
}
