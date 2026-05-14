using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Data;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class TimelinePage : UserControl
{
    private const double TimelineSkeletonRowHeight = 60;
    private TimelineViewModel? _vm;
    private bool _updatingFillerRows;

    public TimelinePage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            DrawTodayLine();
            UpdateTimelineFillerRows();
        };
        SizeChanged += (_, _) =>
        {
            DrawTodayLine();
            UpdateTimelineFillerRows();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as TimelineViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            TimelineTabBar.SelectedTab = _vm.ActiveTab == "Stages" ? "Stages" : "Tasks";
            UpdateTabVisibility();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineViewModel.TodayFraction)
                           or nameof(TimelineViewModel.TaskRows)
                           or nameof(TimelineViewModel.StageRows)
                           or nameof(TimelineViewModel.DayHeaders))
        {
            Dispatcher.BeginInvoke(DrawTodayLine);
            Dispatcher.BeginInvoke(new Action(UpdateTimelineFillerRows));
        }
    }

    private void TimelineTab_SelectedTabChanged(object? sender, string tag)
    {
        if (_vm is not null) _vm.ActiveTab = tag;
        UpdateTabVisibility();
    }

    private void UpdateTabVisibility()
    {
        var isTask = _vm?.ActiveTab == "Tasks";
        TasksSection.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        StagesSection.Visibility = isTask ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(DrawTodayLine, System.Windows.Threading.DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(UpdateTimelineFillerRows), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        DrawTodayLine();
        if (e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0)
            UpdateTimelineFillerRows();
    }

    private void UpdateTimelineFillerRows()
    {
        if (_updatingFillerRows) return;
        try
        {
            _updatingFillerRows = true;
            UpdateTimelineFillerRows(TaskRowsItemsControl, TaskFillerRows);
            UpdateTimelineFillerRows(StageRowsItemsControl, StageFillerRows);
        }
        finally
        {
            _updatingFillerRows = false;
        }
    }

    private void UpdateTimelineFillerRows(ItemsControl rows, ItemsControl fillerRows)
    {
        var scrollViewer = FindVisualParent<ScrollViewer>(rows);
        var viewportHeight = scrollViewer?.ViewportHeight ?? 0;
        if (viewportHeight <= 0)
            viewportHeight = scrollViewer?.ActualHeight ?? 0;

        var rowsHeight = rows.Items.Count == 0 ? 0 : rows.ActualHeight;
        var remainingHeight = viewportHeight - rowsHeight;
        if (remainingHeight < TimelineSkeletonRowHeight / 2)
        {
            if (fillerRows.Visibility == Visibility.Collapsed && fillerRows.ItemsSource == null)
                return;
            fillerRows.Visibility = Visibility.Collapsed;
            fillerRows.ItemsSource = null;
            fillerRows.Height = 0;
            return;
        }

        var fillerCount = Math.Max(1, (int)Math.Ceiling(remainingHeight / TimelineSkeletonRowHeight));
        if (fillerRows.Visibility == Visibility.Visible
            && fillerRows.ItemsSource is int[] existing
            && existing.Length == fillerCount
            && Math.Abs(fillerRows.Height - remainingHeight) < 0.5)
        {
            return;
        }

        var items = new int[fillerCount];
        for (var i = 0; i < fillerCount; i++)
            items[i] = i;

        fillerRows.Height = remainingHeight;
        fillerRows.ItemsSource = items;
        fillerRows.Visibility = Visibility.Visible;
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.ShowCenteredOverlay(new TimelineHelpOverlay(), 760);
    }

    private void DrawTodayLine()
    {
        TodayLineCanvas.Children.Clear();

        if (_vm is null) return;
        double fraction = _vm.TodayFraction;
        if (fraction < 0 || fraction > 1) return;

        var dayHeaderItems = TasksSection.Visibility == Visibility.Visible
            ? TasksDayHeaderItems
            : StagesDayHeaderItems;

        if (dayHeaderItems.ActualWidth <= 0) return;

        var headerOrigin = dayHeaderItems.TransformToVisual(TodayLineCanvas).Transform(new Point(0, 0));
        double timelineWidth = dayHeaderItems.ActualWidth;
        double dayColumnCenter = headerOrigin.X + (fraction * timelineWidth);
        double headerBottom = headerOrigin.Y + dayHeaderItems.ActualHeight;

        var scrollViewer = TasksSection.Visibility == Visibility.Visible
            ? FindVisualChild<ScrollViewer>(TasksSection)
            : FindVisualChild<ScrollViewer>(StagesSection);

        double contentHeight = scrollViewer?.ExtentHeight ?? 0;
        double viewportHeight = scrollViewer?.ViewportHeight ?? 0;

        double height = headerBottom + Math.Max(contentHeight, viewportHeight);

        TodayLineCanvas.Width = headerOrigin.X + timelineWidth;
        TodayLineCanvas.Height = height;

        var line = new Line
        {
            X1 = dayColumnCenter,
            Y1 = headerBottom,
            X2 = dayColumnCenter,
            Y2 = height,
            Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Opacity = 0.9
        };
        TodayLineCanvas.Children.Add(line);

        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock
            {
                Text = "Сегодня",
                FontSize = 9,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            }
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lblW = label.DesiredSize.Width;

        Canvas.SetLeft(label, dayColumnCenter - lblW / 2);
        Canvas.SetTop(label, 2);
        TodayLineCanvas.Children.Add(label);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;

            var resultFromChild = FindVisualChild<T>(child);
            if (resultFromChild != null)
                return resultFromChild;
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T result)
                return result;

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not TimelineTaskRow row) return;
        e.Handled = true;
        _ = OpenTaskRowDetailAsync(row);
    }

    private async Task OpenTaskRowDetailAsync(TimelineTaskRow row)
    {
        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        ProjectSummaryPanel? projectPanel = null;
        UIElement? leftPanel = null;
        var project = await tasksVm.GetProjectForTaskAsync(row.Task.ProjectId);
        if (project is not null)
        {
            projectPanel = new ProjectSummaryPanel();
            projectPanel.SetProject(project);
            leftPanel = projectPanel;
        }

        var projectId = row.Task.ProjectId;
        var overlay = new TaskDetailOverlay();
        overlay.SetTask(
            row.Task,
            () =>
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    if (_vm is not null)
                        await _vm.LoadAsync();
                    var p = await tasksVm.GetProjectForTaskAsync(projectId);
                    if (p is not null && projectPanel is not null)
                        projectPanel.SetProject(p);
                });
            },
            TaskDetailOverlay.TaskDetailDrawerMode.WithProjectSummary);

        if (leftPanel is not null)
            MainWindow.Instance?.ShowDrawer(leftPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
        else
            MainWindow.Instance?.ShowDrawer(overlay, MainWindow.TaskOrStageDetailDrawerWidth);
    }

    private void StageRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not TimelineStageRow row) return;
        e.Handled = true;
        OpenStageRowDetail(row);
    }

    private void OpenStageRowDetail(TimelineStageRow row)
    {
        if (row.ParentTask is null) return;

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(row.ParentTask);

        var overlay = new StageDetailOverlay();
        var taskId = row.ParentTask.Id;
        overlay.SetStage(row.Stage, row.ParentTask, () =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (_vm is not null)
                    await _vm.LoadAsync();
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
}
