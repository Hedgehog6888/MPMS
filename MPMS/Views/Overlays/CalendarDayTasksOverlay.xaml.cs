using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CalendarDayTasksOverlay : UserControl
{
    private Action<LocalTask>? _onTaskSelected;
    private Action<LocalTaskStage, LocalTask>? _onStageSelected;

    public CalendarDayTasksOverlay()
    {
        InitializeComponent();
    }

    /// <summary>Заполняет оверлей: дата, задачи и этапы на день, действия по клику.</summary>
    public void SetDay(
        DateTime day,
        IReadOnlyList<LocalTask> tasks,
        IReadOnlyList<CalendarDayStage> dayStages,
        Action<LocalTask> onTaskSelected,
        Action<LocalTaskStage, LocalTask> onStageSelected)
    {
        _onTaskSelected = onTaskSelected;
        _onStageSelected = onStageSelected;
        var ci = CultureInfo.GetCultureInfo("ru-RU");

        var weekdayDate = day.ToString("dddd, d MMMM yyyy", ci);
        TitleText.Text = weekdayDate.Length > 0
            ? char.ToUpper(weekdayDate[0], ci) + weekdayDate[1..]
            : weekdayDate;

        if (tasks.Count == 0 && dayStages.Count == 0)
        {
            SubtitleText.Visibility = Visibility.Collapsed;
            TabsBorder.Visibility = Visibility.Collapsed;
            FullEmptyState.Visibility = Visibility.Visible;
            TasksTabPanel.Visibility = Visibility.Collapsed;
            StagesTabPanel.Visibility = Visibility.Collapsed;
            TasksList.ItemsSource = null;
            StagesList.ItemsSource = null;
            return;
        }

        SubtitleText.Visibility = Visibility.Visible;
        SubtitleText.Text = FormatSummaryLine(tasks.Count, dayStages.Count);

        FullEmptyState.Visibility = Visibility.Collapsed;
        TabsBorder.Visibility = Visibility.Visible;

        var orderedTasks = tasks
            .OrderBy(t => t.Status)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        TasksList.ItemsSource = orderedTasks;

        var orderedStages = dayStages
            .OrderBy(ds => ds.Stage.Status)
            .ThenBy(ds => ds.Stage.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        StagesList.ItemsSource = orderedStages;

        ApplyTasksEmptyUi(orderedTasks.Count);
        ApplyStagesEmptyUi(orderedStages.Count);

        if (orderedTasks.Count > 0)
        {
            TabTasks.IsChecked = true;
            TasksTabPanel.Visibility = Visibility.Visible;
            StagesTabPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            TabStages.IsChecked = true;
            TasksTabPanel.Visibility = Visibility.Collapsed;
            StagesTabPanel.Visibility = Visibility.Visible;
        }
    }

    private static string FormatSummaryLine(int taskCount, int stageCount)
    {
        var tasksPart  = FormatTaskCount(taskCount);
        var stagesPart = FormatStageCount(stageCount);
        return $"{tasksPart} · {stagesPart}";
    }

    private static string FormatTaskCount(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return $"{n} задача";
        if (mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14)) return $"{n} задачи";
        return $"{n} задач";
    }

    private static string FormatStageCount(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return $"{n} этап";
        if (mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14)) return $"{n} этапа";
        return $"{n} этапов";
    }

    private void ApplyTasksEmptyUi(int count)
    {
        if (count > 0)
        {
            TasksList.Visibility = Visibility.Visible;
            TasksEmptyState.Visibility = Visibility.Collapsed;
        }
        else
        {
            TasksList.Visibility = Visibility.Collapsed;
            TasksEmptyState.Visibility = Visibility.Visible;
        }
    }

    private void ApplyStagesEmptyUi(int count)
    {
        if (count > 0)
        {
            StagesList.Visibility = Visibility.Visible;
            StagesEmptyState.Visibility = Visibility.Collapsed;
        }
        else
        {
            StagesList.Visibility = Visibility.Collapsed;
            StagesEmptyState.Visibility = Visibility.Visible;
        }
    }

    private void TabTasks_Click(object sender, RoutedEventArgs e)
    {
        TasksTabPanel.Visibility = Visibility.Visible;
        StagesTabPanel.Visibility = Visibility.Collapsed;
    }

    private void TabStages_Click(object sender, RoutedEventArgs e)
    {
        TasksTabPanel.Visibility = Visibility.Collapsed;
        StagesTabPanel.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not LocalTask task) return;
        e.Handled = true;
        _onTaskSelected?.Invoke(task);
    }

    private void StageRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not CalendarDayStage row) return;
        e.Handled = true;
        _onStageSelected?.Invoke(row.Stage, row.ParentTask);
    }
}
