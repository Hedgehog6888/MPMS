using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MPMS.Data;
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

    private StageItem? _draggedStageItem;
    private Point _stageDragStartPoint;
    private bool _isStageDragging;

    public ProjectDetailPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
            UpdateMarkProjectButton();
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectDetailViewModel.Project))
            Dispatcher.InvokeAsync(UpdateMarkProjectButton);
    }

    private string _userRole = "";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        _userRole = auth.UserRole ?? "";
        _canEdit = _userRole is "Administrator" or "Project Manager" or "Foreman";
        bool isManagerOrAbove = _userRole is "Administrator" or "Project Manager";
        bool canMarkProject   = _userRole is "Administrator" or "Project Manager";

        EditProjectBtn.Visibility      = isManagerOrAbove ? Visibility.Visible : Visibility.Collapsed;
        MarkProjectBtn.Visibility      = canMarkProject ? Visibility.Visible : Visibility.Collapsed;
        CreateTaskBtn.Visibility       = Visibility.Collapsed; // shown only on Tasks tab for editors
        CreateStageBtn.Visibility      = Visibility.Collapsed; // shown only on Stages tab for editors
        CreateTaskQuickBtn.Visibility  = _canEdit ? Visibility.Visible : Visibility.Collapsed;
        _ = Dispatcher.InvokeAsync(UpdateMarkProjectButton, System.Windows.Threading.DispatcherPriority.Loaded);
        _ = Dispatcher.InvokeAsync(SyncStageViewToggleIcons, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void SyncStageViewToggleIcons()
    {
        // Не вызывать из Checked/Unchecked радиокнопки: при IsChecked из XAML событие идёт до IComponentConnector для Image.
        if (StageViewListRb is null || StageViewListIconDark is null || StageViewListIconLight is null
            || StageViewKanbanRb is null || StageViewKanbanIconDark is null || StageViewKanbanIconLight is null)
            return;

        bool listOn = StageViewListRb.IsChecked == true;
        StageViewListIconDark.Visibility = listOn ? Visibility.Collapsed : Visibility.Visible;
        StageViewListIconLight.Visibility = listOn ? Visibility.Visible : Visibility.Collapsed;
        bool kanbanOn = StageViewKanbanRb.IsChecked == true;
        StageViewKanbanIconDark.Visibility = kanbanOn ? Visibility.Collapsed : Visibility.Visible;
        StageViewKanbanIconLight.Visibility = kanbanOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void MarkProject_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        await VM.MarkProjectForDeletionCommand.ExecuteAsync(null);
        // Update button text based on project state
        UpdateMarkProjectButton();
    }

    private void UpdateMarkProjectButton()
    {
        if (VM?.Project is null) return;
        bool marked = VM.Project.IsMarkedForDeletion;
        // Update mark button text via template
        MarkProjectBtn.ApplyTemplate();
        if (MarkProjectBtn.Template?.FindName("MarkBtnText", MarkProjectBtn) is System.Windows.Controls.TextBlock tb)
            tb.Text = marked ? "Снять пометку удаления" : "Пометить к удалению";
        // Hide editing buttons when project is marked for deletion
        EditProjectBtn.Visibility  = marked ? Visibility.Collapsed : (_userRole is "Administrator" or "Project Manager" ? Visibility.Visible : Visibility.Collapsed);
        CreateTaskQuickBtn.Visibility = marked ? Visibility.Collapsed : (_canEdit ? Visibility.Visible : Visibility.Collapsed);
        CreateTaskBtn.Visibility  = marked ? Visibility.Collapsed : (TasksPanel.Visibility == Visibility.Visible && _canEdit ? Visibility.Visible : Visibility.Collapsed);
        CreateStageBtn.Visibility = marked ? Visibility.Collapsed : (StagesPanel.Visibility == Visibility.Visible && _canEdit ? Visibility.Visible : Visibility.Collapsed);
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

        InfoPanel.Visibility       = tab == "Info"       ? Visibility.Visible : Visibility.Collapsed;
        TasksPanel.Visibility      = tab == "Tasks"      ? Visibility.Visible : Visibility.Collapsed;
        StagesPanel.Visibility     = tab == "Stages"     ? Visibility.Visible : Visibility.Collapsed;
        DiscussionPanel.Visibility = tab == "Discussion" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility      = tab == "Files"      ? Visibility.Visible : Visibility.Collapsed;
        MaterialsPanel.Visibility  = tab == "Materials"  ? Visibility.Visible : Visibility.Collapsed;

        StageViewModeSwitcher.Visibility = tab == "Stages" ? Visibility.Visible : Visibility.Collapsed;
        if (tab == "Stages" && VM is not null)
        {
            bool listMode = VM.StageViewMode == "List";
            StageViewListRb.IsChecked = listMode;
            StageViewKanbanRb.IsChecked = !listMode;
            StageListHost.Visibility = listMode ? Visibility.Visible : Visibility.Collapsed;
            StageKanbanPanel.Visibility = listMode ? Visibility.Collapsed : Visibility.Visible;
            SyncStageViewToggleIcons();
        }

        bool marked = VM?.Project?.IsMarkedForDeletion ?? false;
        CreateTaskBtn.Visibility  = (tab == "Tasks"  && _canEdit && !marked) ? Visibility.Visible : Visibility.Collapsed;
        CreateStageBtn.Visibility = (tab == "Stages" && _canEdit && !marked) ? Visibility.Visible : Visibility.Collapsed;

        if (tab == "Discussion")
        {
            Dispatcher.InvokeAsync(() =>
                ProjectMessagesScrollViewer.ScrollToBottom(),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void TaskView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string mode) return;
        VM?.SwitchTaskViewCommand.Execute(mode);

        TaskListPanel.Visibility   = mode == "List"   ? Visibility.Visible : Visibility.Collapsed;
        TaskKanbanPanel.Visibility = mode == "Kanban" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StageView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string mode) return;
        VM?.SwitchStageViewCommand.Execute(mode);

        StageListHost.Visibility = mode == "List" ? Visibility.Visible : Visibility.Collapsed;
        StageKanbanPanel.Visibility = mode == "Kanban" ? Visibility.Visible : Visibility.Collapsed;
        SyncStageViewToggleIcons();
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.Project is null) return;
        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        var overlay = new CreateTaskOverlay();
        var vm = VM;
        overlay.SetCreateMode(tasksVm, vm.Project.Id,
            onSaved: async () =>
            {
                if (vm != null)
                {
                    await vm.LoadAsync();
                    _ = Dispatcher.InvokeAsync(UpdateMarkProjectButton);
                }
            });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.Project is null) return;
        var projVm = App.Services.GetRequiredService<ProjectsViewModel>();
        var overlay = new CreateProjectOverlay();
        var vm = VM;
        overlay.SetEditMode(projVm, vm.Project,
            onSaved: async () =>
            {
                if (vm != null)
                {
                    await vm.LoadAsync();
                    _ = Dispatcher.InvokeAsync(UpdateMarkProjectButton);
                }
            });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var overlay = new CreateTaskOverlay();
        var currentTask = task;
        overlay.SetEditMode(
            currentTask,
            onSaved: async () =>
            {
                await VM.LoadAsync();
                _ = Dispatcher.InvokeAsync(UpdateMarkProjectButton);
            },
            onAfterSave: () => OpenTaskDetail(currentTask));
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var owner = Window.GetWindow(this);
        if (Dialogs.ConfirmDeleteDialog.Show(owner, "Задача", task.Name))
            await VM.DeleteTaskCommand.ExecuteAsync(task);
    }

    private async void MarkTaskForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        await VM.MarkTaskForDeletionCommand.ExecuteAsync(task);
    }

    private async void MarkStageForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        await VM.MarkStageForDeletionCommand.ExecuteAsync(stage);
    }

    private async void StartStageFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((stage, StageStatus.InProgress));
    }

    private async void CompleteStageFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((stage, StageStatus.Completed));
    }

    private async void RevertStageFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((stage, StageStatus.Planned));
    }

    private async void ReopenStageFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        e.Handled = true;
        await VM.ChangeStageStatusCommand.ExecuteAsync((stage, StageStatus.InProgress));
    }

    private async void DeleteStageFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        var owner = Window.GetWindow(this);
        if (Dialogs.ConfirmDeleteDialog.Show(owner, "Этап", stage.Name))
            await VM.DeleteStageCommand.ExecuteAsync(stage);
    }

    private void Stage_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalTaskStage stage || VM is null) return;
        var task = VM.Tasks.FirstOrDefault(t => t.Id == stage.TaskId);
        if (task is null) return;

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(task);

        var overlay = new StageDetailOverlay();
        var vm = VM;
        var stageItem = new ViewModels.StageItem { Stage = stage, TaskName = stage.TaskName };
        var taskId = task.Id;
        overlay.SetStage(stageItem, task, () =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (vm != null)
                {
                    await vm.LoadAsync();
                    UpdateMarkProjectButton();
                }
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var updatedTask = await db.Tasks.FindAsync(taskId);
                if (updatedTask != null)
                    await Dispatcher.InvokeAsync(() => taskPanel.SetTask(updatedTask));
            });
        });
        MainWindow.Instance?.ShowDrawer(taskPanel, overlay, 850);
    }

    private void OpenStageKanbanDetail(StageItem item)
    {
        if (VM is null) return;
        var task = VM.Tasks.FirstOrDefault(t => t.Id == item.TaskId);
        if (task is null) return;

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(task);

        var overlay = new StageDetailOverlay();
        var taskId = task.Id;
        var vm = VM;
        overlay.SetStage(item, task, () =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                if (vm is not null)
                {
                    await vm.LoadAsync();
                    UpdateMarkProjectButton();
                }
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var updatedTask = await db.Tasks.FindAsync(taskId);
                if (updatedTask != null)
                    await Dispatcher.InvokeAsync(() => taskPanel.SetTask(updatedTask));
            });
        });
        MainWindow.Instance?.ShowDrawer(taskPanel, overlay, 850);
    }

    private void StageKanbanCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is StageItem item)
        {
            _draggedStageItem = item;
            _stageDragStartPoint = e.GetPosition(null);
            _isStageDragging = false;
        }
    }

    private void StageKanbanCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedStageItem is null || !_draggedStageItem.CanDragInKanban ||
            e.LeftButton != MouseButtonState.Pressed || _isStageDragging)
            return;

        var currentPos = e.GetPosition(null);
        var diff = _stageDragStartPoint - currentPos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isStageDragging = true;
            if (sender is DependencyObject dep)
            {
                var data = new DataObject("KanbanStage", _draggedStageItem);
                DragDrop.DoDragDrop(dep, data, DragDropEffects.Move);
            }
            _isStageDragging = false;
            _draggedStageItem = null;
        }
    }

    private void StageKanbanCard_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isStageDragging && _draggedStageItem is not null &&
            sender is FrameworkElement fe && fe.DataContext is StageItem item)
        {
            OpenStageKanbanDetail(item);
        }
        _draggedStageItem = null;
        _isStageDragging = false;
    }

    private void StageKanbanColumn_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("KanbanStage") && sender is Border border)
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

    private void StageKanbanColumn_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
            border.BorderThickness = new Thickness(1);
        }
    }

    private async void StageKanbanColumn_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
            border.BorderThickness = new Thickness(1);
        }

        if (!e.Data.GetDataPresent("KanbanStage")) return;
        if (e.Data.GetData("KanbanStage") is not StageItem item) return;
        if (sender is not FrameworkElement fe || fe.Tag is not string statusStr) return;
        if (VM is null) return;

        if (!item.CanDragInKanban) return;

        var newStatus = statusStr switch
        {
            "InProgress" => StageStatus.InProgress,
            "Completed"  => StageStatus.Completed,
            _            => StageStatus.Planned
        };

        if (item.Stage.Status == newStatus)
        {
            e.Handled = true;
            return;
        }

        await VM.ChangeStageStatusCommand.ExecuteAsync((item.Stage, newStatus));

        e.Handled = true;
    }

    private void EditStageFromProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || VM is null) return;
        var task = VM.Tasks.FirstOrDefault(t => t.Id == stage.TaskId);
        if (task is null) return;
        var overlay = new CreateStageOverlay();
        var vm = VM;
        overlay.SetEditMode(stage, task,
            onSaved: async () =>
            {
                if (vm != null)
                {
                    await vm.LoadAsync();
                    _ = Dispatcher.InvokeAsync(UpdateMarkProjectButton);
                }
            });
        MainWindow.Instance?.ShowDrawer(overlay);
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
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await vm.LoadAsync();
                    UpdateMarkProjectButton();
                });
        }, TaskDetailOverlay.TaskDetailDrawerMode.TaskOnly);
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

    private void AddProjectStage_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.Project is null) return;
        var overlay = new CreateStageOverlay();
        var vm = VM;
        overlay.SetCreateModeForProject(vm.Project.Id,
            onSaved: async () => { if (vm != null) await vm.LoadAsync(); });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void SendProjectMessage_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null || ProjectMessageInput is null) return;
        var text = ProjectMessageInput.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        ProjectMessageInput.Text = "";
        await VM.SendMessageAsync(text);
        _ = Dispatcher.InvokeAsync(() =>
            ProjectMessagesScrollViewer.ScrollToBottom(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ProjectMessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendProjectMessage_Click(sender, e);
            e.Handled = true;
        }
    }

    // ── Local search box focus animations (matches global search style) ─────────
    private static readonly SolidColorBrush SearchFocusBrush = new(Colors.Black);
    private static readonly SolidColorBrush SearchNormalBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush SearchFocusBg = new(Colors.White);
    private static readonly SolidColorBrush SearchNormalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));
    private static readonly System.Windows.Media.Effects.DropShadowEffect SearchFocusShadow = new()
    {
        Color = Colors.Black, BlurRadius = 6, Opacity = 0.10, ShadowDepth = 0
    };

    private static Border? FindParentBorder(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is Border b) return b;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void LocalSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var border = FindParentBorder(tb);
            if (border is not null)
            {
                border.BorderBrush = SearchFocusBrush;
                border.Background = SearchFocusBg;
                border.Effect = SearchFocusShadow;
            }
        }
    }

    private void LocalSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var border = FindParentBorder(tb);
            if (border is not null)
            {
                border.BorderBrush = SearchNormalBrush;
                border.Background = SearchNormalBg;
                border.Effect = null;
            }
        }
    }

    private void ClearTaskSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectDetailViewModel vm) vm.TaskSearchText = string.Empty;
    }

    private void ClearStageSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectDetailViewModel vm) vm.StageSearchText = string.Empty;
    }
}
