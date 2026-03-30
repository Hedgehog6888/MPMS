using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class CalendarDayTasksOverlay : UserControl
{
    private Action<LocalTask>? _onTaskSelected;

    public CalendarDayTasksOverlay()
    {
        InitializeComponent();
    }

    /// <summary>Заполняет оверлей: дата, список задач и действие по клику на задачу.</summary>
    public void SetDay(DateTime day, IReadOnlyList<LocalTask> tasks, Action<LocalTask> onTaskSelected)
    {
        _onTaskSelected = onTaskSelected;
        var ci = CultureInfo.GetCultureInfo("ru-RU");

        var weekdayDate = day.ToString("dddd, d MMMM yyyy", ci);
        TitleText.Text = weekdayDate.Length > 0
            ? char.ToUpper(weekdayDate[0], ci) + weekdayDate[1..]
            : weekdayDate;

        if (tasks.Count == 0)
        {
            SubtitleText.Visibility = Visibility.Collapsed;
            TasksList.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            TasksList.ItemsSource = null;
            return;
        }

        SubtitleText.Visibility = Visibility.Visible;
        SubtitleText.Text = FormatTaskCount(tasks.Count);
        var ordered = tasks.OrderBy(t => t.Status).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        TasksList.ItemsSource = ordered;
        TasksList.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
    }

    private static string FormatTaskCount(int n)
    {
        var mod10 = n % 10;
        var mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11) return $"{n} задача";
        if (mod10 is >= 2 and <= 4 && (mod100 < 12 || mod100 > 14)) return $"{n} задачи";
        return $"{n} задач";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not LocalTask task) return;
        e.Handled = true;
        _onTaskSelected?.Invoke(task);
    }
}
