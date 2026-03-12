using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Pages;

public partial class TasksPage : UserControl
{
    private LocalTask? _draggedTask;
    private Point _dragStartPoint;
    private bool _isDragging;
    public TasksPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        bool canCreate = role is "Admin" or "Administrator"
                              or "ProjectManager" or "Manager" or "Project Manager";
        CreateTaskBtn.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private TasksViewModel? VM => DataContext as TasksViewModel;

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string mode) return;
        if (VM is not null) VM.ViewMode = mode;

        ListPanel.Visibility   = mode == "List"   ? Visibility.Visible : Visibility.Collapsed;
        KanbanPanel.Visibility = mode == "Kanban" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetCreateMode(VM);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetEditMode(task, async () => await VM.LoadAsync());
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var result = MessageBox.Show($"Удалить задачу «{task.Name}»?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            await VM.DeleteTaskCommand.ExecuteAsync(task);
    }

    private void Task_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalTask task) return;
        OpenTaskDetail(task);
    }

    private async void OpenTaskDetail(LocalTask task)
    {
        var overlay = new TaskDetailOverlay();
        var vm = VM;
        overlay.SetTask(task, () =>
        {
            if (vm != null)
                _ = Application.Current.Dispatcher.InvokeAsync(async () => await vm.LoadAsync());
        });

        UIElement? leftPanel = null;
        if (vm != null)
        {
            var project = await vm.GetProjectForTaskAsync(task.ProjectId);
            if (project != null)
            {
                var projectPanel = new ProjectSummaryPanel();
                projectPanel.SetProject(project);
                leftPanel = projectPanel;
            }
        }
        MainWindow.Instance?.ShowDrawer(leftPanel, overlay, 900);
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        var anim = new DoubleAnimation(36, 220, TimeSpan.FromMilliseconds(250))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        SearchBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var anim = new DoubleAnimation(220, 36, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        SearchBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void KanbanCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LocalTask task)
        {
            _draggedTask = task;
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }
    }

    private void KanbanCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedTask is null || e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
        var currentPos = e.GetPosition(null);
        var diff = _dragStartPoint - currentPos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            if (sender is DependencyObject dep)
                DragDrop.DoDragDrop(dep, new DataObject("KanbanTask", _draggedTask), DragDropEffects.Move);
            _isDragging = false;
            _draggedTask = null;
        }
    }

    private void KanbanCard_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging && _draggedTask is not null && sender is FrameworkElement fe && fe.DataContext is LocalTask task)
            OpenTaskDetail(task);
        _draggedTask = null;
        _isDragging = false;
    }

    private void KanbanColumn_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("KanbanTask") && sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2));
            border.BorderThickness = new Thickness(2);
            e.Effects = DragDropEffects.Move;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void KanbanColumn_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
            border.BorderThickness = new Thickness(1);
        }
    }

    private async void KanbanColumn_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
            border.BorderThickness = new Thickness(1);
        }
        if (!e.Data.GetDataPresent("KanbanTask") || e.Data.GetData("KanbanTask") is not LocalTask task) return;
        if (sender is not FrameworkElement fe || fe.Tag is not string statusStr) return;
        if (VM is null) return;
        var newStatus = statusStr switch
        {
            "InProgress" => TaskStatus.InProgress,
            "Paused" => TaskStatus.Paused,
            "Completed" => TaskStatus.Completed,
            _ => TaskStatus.Planned
        };
        if (task.Status != newStatus)
            await VM.MoveTaskCommand.ExecuteAsync((task, newStatus));
        e.Handled = true;
    }
}
