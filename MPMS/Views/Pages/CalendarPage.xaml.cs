using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
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
    }

    /// <summary>Click on a day cell → centered overlay with all tasks for that date.</summary>
    private void Cell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not CalendarCell cell) return;
        if (cell.IsEmpty) return;

        e.Handled = true;

        var overlay = new CalendarDayTasksOverlay();
        overlay.SetDay(cell.Date, cell.Tasks, OpenTaskDetail);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 480);
    }

    /// <summary>Click on a task chip inside a cell → open TaskDetailOverlay directly.</summary>
    private void TaskChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not LocalTask task) return;
        e.Handled = true;

        OpenTaskDetail(task);
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
}
