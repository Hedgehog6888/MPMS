using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Infrastructure;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class TaskSummaryPanel : UserControl
{
    public TaskSummaryPanel()
    {
        InitializeComponent();
    }

    public void SetTask(LocalTask? task)
    {
        if (task is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        TaskNameText.Text = task.Name;
        AssigneeText.Text = task.AssignedUserName ?? "—";
        DueDateText.Text = task.DueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
        ProgressBar.Value = task.ProgressPercent;
        ProgressText.Text = $"{task.ProgressPercent}%";
        ProgressBar.Foreground = ProgressPercentToBrushConverter.Instance.Convert(task.ProgressPercent, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush ?? Brushes.Gray;

        var statusBrush = TaskStatusToBrushConverter.Instance.Convert(task.Status, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = statusBrush ?? Brushes.Gray;
        StatusText.Text = TaskStatusToStringConverter.Instance.Convert(task.Status, typeof(string), null, CultureInfo.InvariantCulture) as string ?? "—";
    }
}
