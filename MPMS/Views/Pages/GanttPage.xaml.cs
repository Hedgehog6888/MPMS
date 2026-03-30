using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class GanttPage : UserControl
{
    private GanttViewModel? _vm;

    public GanttPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded             += (_, _) => DrawTodayLine();
        SizeChanged        += (_, _) => DrawTodayLine();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as GanttViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            TabTasks.IsChecked   = _vm.ActiveTab == "Tasks";
            TabStages.IsChecked  = _vm.ActiveTab == "Stages";
            UpdateTabVisibility();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GanttViewModel.TodayFraction)
                           or nameof(GanttViewModel.TaskRows)
                           or nameof(GanttViewModel.StageRows)
                           or nameof(GanttViewModel.DayHeaders))
            Dispatcher.BeginInvoke(DrawTodayLine);
    }

    private void TabTasks_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.ActiveTab = "Tasks";
        UpdateTabVisibility();
    }

    private void TabStages_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.ActiveTab = "Stages";
        UpdateTabVisibility();
    }

    private void UpdateTabVisibility()
    {
        var isTask = _vm?.ActiveTab == "Tasks";
        TasksSection.Visibility  = isTask ? Visibility.Visible   : Visibility.Collapsed;
        StagesSection.Visibility = isTask ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(DrawTodayLine);
    }

    private void DrawTodayLine()
    {
        TodayLineCanvas.Children.Clear();

        if (_vm is null) return;
        double fraction = _vm.TodayFraction;
        if (fraction < 0 || fraction > 1) return;

        // Find the timeline column (right of the 260px info column)
        const double leftColWidth  = 260;
        const double headerHeight  = 37; // approx day-header row height
        double totalWidth          = ActualWidth - 16 - 16; // margins 16+16
        if (totalWidth <= leftColWidth) return;

        double timelineWidth = totalWidth - leftColWidth;
        double lineX = leftColWidth + fraction * timelineWidth;

        bool isTasks  = _vm.ActiveTab == "Tasks";
        int rowCount  = isTasks ? _vm.TaskRows.Count : _vm.StageRows.Count;
        double height = headerHeight + rowCount * 52 + 10;

        TodayLineCanvas.Width  = totalWidth;
        TodayLineCanvas.Height = height;

        // Vertical dashed line (red)
        var line = new Line
        {
            X1 = lineX, Y1 = headerHeight,
            X2 = lineX, Y2 = height,
            Stroke          = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Opacity         = 0.9
        };
        TodayLineCanvas.Children.Add(line);

        // "Сегодня" label (red)
        var label = new Border
        {
            Background  = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(4, 2, 4, 2),
            Child        = new TextBlock
            {
                Text       = "Сегодня",
                FontSize   = 9,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            }
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lblW = label.DesiredSize.Width;
        Canvas.SetLeft(label, lineX - lblW / 2);
        Canvas.SetTop(label, 6);
        TodayLineCanvas.Children.Add(label);
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not GanttTaskRow row) return;
        e.Handled = true;
        var overlay = new TaskDetailOverlay();
        overlay.SetTask(row.Task, onClosed: null, TaskDetailOverlay.TaskDetailDrawerMode.WithProjectSummary);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void StageRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not GanttStageRow row) return;
        e.Handled = true;
        var overlay = new StageDetailOverlay();
        overlay.SetStage(row.Stage, row.ParentTask!, onClosed: null);
        MainWindow.Instance?.ShowDrawer(overlay);
    }
}
