using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Pages;

public partial class ProjectDetailPage : UserControl
{
    private bool _canEdit;
    private LocalTask? _draggedTask;
    private Point _dragStartPoint;
    private bool _isDragging;

    public ProjectDetailPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        _canEdit = role is "Admin" or "Administrator"
                        or "ProjectManager" or "Manager" or "Project Manager";
        EditProjectBtn.Visibility      = _canEdit ? Visibility.Visible : Visibility.Collapsed;
        CreateTaskBtn.Visibility       = Visibility.Collapsed; // shown only on Tasks tab for editors
        CreateTaskQuickBtn.Visibility  = _canEdit ? Visibility.Visible : Visibility.Collapsed;
    }

    private ProjectDetailViewModel? VM => DataContext as ProjectDetailViewModel;

    private void Back_Click(object sender, MouseButtonEventArgs e)
    {
        VM?.GoBackCommand.Execute(null);
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tab) return;
        VM?.SwitchTabCommand.Execute(tab);

        InfoPanel.Visibility      = tab == "Info"      ? Visibility.Visible : Visibility.Collapsed;
        TasksPanel.Visibility     = tab == "Tasks"     ? Visibility.Visible : Visibility.Collapsed;
        StagesPanel.Visibility    = tab == "Stages"    ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility     = tab == "Files"     ? Visibility.Visible : Visibility.Collapsed;
        MaterialsPanel.Visibility = tab == "Materials" ? Visibility.Visible : Visibility.Collapsed;

        CreateTaskBtn.Visibility = (tab == "Tasks" && _canEdit) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TaskView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string mode) return;
        VM?.SwitchTaskViewCommand.Execute(mode);

        TaskListPanel.Visibility   = mode == "List"   ? Visibility.Visible : Visibility.Collapsed;
        TaskKanbanPanel.Visibility = mode == "Kanban" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.Project is null) return;
        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        var overlay = new CreateTaskOverlay();
        var vm = VM;
        overlay.SetCreateMode(tasksVm, vm.Project.Id,
            onSaved: async () => { if (vm != null) await vm.LoadAsync(); });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.Project is null) return;
        var projVm = App.Services.GetRequiredService<ProjectsViewModel>();
        var overlay = new CreateProjectOverlay();
        var vm = VM;
        overlay.SetEditMode(projVm, vm.Project,
            onSaved: async () => { if (vm != null) await vm.LoadAsync(); });
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

    private void OpenTaskDetail(LocalTask task)
    {
        var overlay = new TaskDetailOverlay();
        var vm = VM;
        overlay.SetTask(task, () =>
        {
            if (vm != null)
                _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    async () => await vm.LoadAsync());
        });
        MainWindow.Instance?.ShowDrawer(overlay, 500);
    }

    // ── Kanban Drag-and-Drop ────────────────────────────────────────────

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
        if (_draggedTask is null || e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var currentPos = e.GetPosition(null);
        var diff = _dragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragging = true;
            if (sender is DependencyObject dep)
            {
                var data = new DataObject("KanbanTask", _draggedTask);
                DragDrop.DoDragDrop(dep, data, DragDropEffects.Move);
            }
            _isDragging = false;
            _draggedTask = null;
        }
    }

    private void KanbanCard_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging && _draggedTask is not null &&
            sender is FrameworkElement fe && fe.DataContext is LocalTask task)
        {
            OpenTaskDetail(task);
        }
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
        else
        {
            e.Effects = DragDropEffects.None;
        }
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

        if (!e.Data.GetDataPresent("KanbanTask")) return;
        if (e.Data.GetData("KanbanTask") is not LocalTask task) return;
        if (sender is not FrameworkElement fe || fe.Tag is not string statusStr) return;
        if (VM is null) return;

        var newStatus = statusStr switch
        {
            "InProgress" => TaskStatus.InProgress,
            "Paused"     => TaskStatus.Paused,
            "Completed"  => TaskStatus.Completed,
            _            => TaskStatus.Planned
        };

        if (task.Status != newStatus)
            await VM.MoveTaskCommand.ExecuteAsync((task, newStatus));

        e.Handled = true;
    }

    private void UploadFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файлы для загрузки",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*|Изображения|*.png;*.jpg;*.jpeg|Документы|*.pdf;*.docx;*.xlsx"
        };
        if (dialog.ShowDialog() == true)
        {
            MessageBox.Show(
                $"Выбрано файлов: {dialog.FileNames.Length}\nЗагрузка будет реализована при подключении к серверу.",
                "Загрузка файлов", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void SendProjectMessage_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null || ProjectMessageInput is null) return;
        var text = ProjectMessageInput.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        ProjectMessageInput.Text = "";
        await VM.SendMessageAsync(text);
    }

    private void ProjectMessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendProjectMessage_Click(sender, e);
            e.Handled = true;
        }
    }
}
