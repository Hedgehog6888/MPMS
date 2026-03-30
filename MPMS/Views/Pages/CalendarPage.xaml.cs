using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Models;
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
    {
        var detail = new TaskDetailOverlay();
        detail.SetTask(
            task,
            onClosed: () => _ = (_vm?.LoadAsync()),
            drawerMode: TaskDetailOverlay.TaskDetailDrawerMode.WithProjectSummary);
        MainWindow.Instance?.ShowDrawer(detail);
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
        var overlay = new StageDetailOverlay();
        overlay.SetStage(item, parentTask, onClosed: () => _ = (_vm?.LoadAsync()));
        MainWindow.Instance?.ShowDrawer(overlay);
    }
}
