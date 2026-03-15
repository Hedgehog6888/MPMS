using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateStageOverlay : UserControl
{
    private TaskDetailViewModel? _vm;
    private LocalTask? _task;
    private LocalTaskStage? _editStage;
    private Func<System.Threading.Tasks.Task>? _onSaved;
    private Action? _onAfterSave;

    private List<AssigneePickerItem> _allAssigneeItems = [];
    private readonly HashSet<Guid> _selectedAssigneeIds = [];
    private bool _isWorkerMode; // работник не выбирает исполнителей — автоматом он сам
    private StageStatus _selectedStatus = StageStatus.Planned;

    public CreateStageOverlay()
    {
        InitializeComponent();
    }

    public void SetTask(LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = task;
        _vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        _vm.SetTask(task);
        _onSaved = onSaved;
        TaskNameLabel.Text = $"Задача: {task.Name}";
        ProjectNameRow.Visibility = Visibility.Visible;
        ProjectNameBox.Text = task.ProjectName ?? "—";
        ProjectTaskPickerRow.Visibility = Visibility.Collapsed;
        _isWorkerMode = IsCurrentUserWorker();
        _ = LoadAssigneesFromTaskAsync(task.Id);
    }

    public void SetCreateModeFromStagesPage(Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = null;
        _vm = null;
        _editStage = null;
        _onSaved = onSaved;
        _isWorkerMode = IsCurrentUserWorker();
        TitleLabel.Text = "Добавить этап";
        SaveButton.Content = "Добавить этап";
        TaskNameLabel.Text = "Выберите проект и задачу";
        ProjectTaskPickerRow.Visibility = Visibility.Visible;
        StatusRow.Visibility = Visibility.Collapsed;
        ApplyWorkerModeUi();
        _ = LoadProjectsAsync();
    }

    public void SetCreateModeForProject(Guid projectId, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = null;
        _vm = null;
        _editStage = null;
        _onSaved = onSaved;
        _isWorkerMode = IsCurrentUserWorker();
        TitleLabel.Text = "Добавить этап";
        SaveButton.Content = "Добавить этап";
        TaskNameLabel.Text = "Выберите задачу";
        ProjectTaskPickerRow.Visibility = Visibility.Visible;
        StatusRow.Visibility = Visibility.Collapsed;
        ProjectCombo.Visibility = Visibility.Collapsed;
        ProjectNameRow.Visibility = Visibility.Visible;
        ApplyWorkerModeUi();
        _ = LoadProjectTasksAsync(projectId);
    }

    public void SetEditMode(LocalTaskStage stage, LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null, Action? onAfterSave = null)
    {
        _editStage = stage;
        _task = task;
        _vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        _vm.SetTask(task);
        _onSaved = onSaved;
        _onAfterSave = onAfterSave;
        _isWorkerMode = IsCurrentUserWorker();
        TitleLabel.Text = "Редактировать этап";
        SaveButton.Content = "Сохранить";
        StatusRow.Visibility = Visibility.Visible;
        TaskNameLabel.Text = $"Задача: {task.Name}";

        NameBox.Text = stage.Name;
        DescriptionBox.Text = stage.Description ?? "";

        _selectedStatus = stage.Status;
        ApplyStatusSelection(_selectedStatus);

        ApplyWorkerModeUi();
        _ = LoadAssigneesFromTaskAsync(task.Id, stage.Id);
    }

    private static bool IsCurrentUserWorker()
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        return string.Equals(auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyWorkerModeUi()
    {
        if (_isWorkerMode)
        {
            AssigneesSection.Visibility = Visibility.Collapsed;
            WorkerAutoAssignHint.Visibility = _editStage is null ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            AssigneesSection.Visibility = Visibility.Visible;
            WorkerAutoAssignHint.Visibility = Visibility.Collapsed;
        }
    }

    private async System.Threading.Tasks.Task LoadAssigneesFromTaskAsync(Guid taskId, Guid? stageId = null)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        var auth = App.Services.GetRequiredService<IAuthService>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // Get task assignees (people who can be assigned to stages of this task)
        var taskAssignees = await db.TaskAssignees
            .Where(ta => ta.TaskId == taskId)
            .OrderBy(ta => ta.UserName)
            .ToListAsync();

        // Also check legacy single-assignee on the task entity
        LocalTask? taskEntity = await db.Tasks.FindAsync(taskId);
        if (taskAssignees.Count == 0 && taskEntity?.AssignedUserId.HasValue == true)
        {
            // Fallback: use task's single assignee
            taskAssignees.Add(new LocalTaskAssignee
            {
                TaskId = taskId,
                UserId = taskEntity.AssignedUserId!.Value,
                UserName = taskEntity.AssignedUserName ?? "—"
            });
        }

        // Работник при создании этапа: автоматом назначается он сам; добавляем в список если его нет
        if (_isWorkerMode && !stageId.HasValue && auth.UserId.HasValue)
        {
            _selectedAssigneeIds.Clear();
            _selectedAssigneeIds.Add(auth.UserId.Value);
            var hasSelf = taskAssignees.Any(ta => ta.UserId == auth.UserId.Value);
            if (!hasSelf)
            {
                var self = await db.Users.FindAsync(auth.UserId.Value);
                taskAssignees.Insert(0, new LocalTaskAssignee
                {
                    TaskId = taskId,
                    UserId = auth.UserId.Value,
                    UserName = self?.Name ?? auth.UserName ?? "—"
                });
            }
        }

        // Populate AvatarPath from Users
        var userIds = taskAssignees.Select(ta => ta.UserId).Distinct().ToList();
        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.AvatarPath);
        foreach (var ta in taskAssignees)
            ta.AvatarPath = userAvatars.GetValueOrDefault(ta.UserId);

        // Only task assignees (project members assigned to task) can be assigned to stages
        if (taskAssignees.Count == 0)
        {
            _allAssigneeItems = [];
        }
        else
        {
            _allAssigneeItems = taskAssignees.Select(ta => new AssigneePickerItem(
                ta.UserId, ta.UserName, "Worker", _selectedAssigneeIds, ta.AvatarPath)).ToList();
        }

        // Load existing stage assignees if editing
        if (stageId.HasValue)
        {
            var stageAssignees = await db.StageAssignees
                .Where(sa => sa.StageId == stageId.Value)
                .ToListAsync();
            foreach (var sa in stageAssignees)
                _selectedAssigneeIds.Add(sa.UserId);

            // Also check legacy single assignee
            var stageEntity = await db.TaskStages.FindAsync(stageId.Value);
            if (stageEntity?.AssignedUserId.HasValue == true && !_selectedAssigneeIds.Contains(stageEntity.AssignedUserId!.Value))
                _selectedAssigneeIds.Add(stageEntity.AssignedUserId.Value);
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            RefreshAssigneeItems();
            RefreshAssigneeChips();
            NoAssigneesHint.Visibility = _allAssigneeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyWorkerModeUi();
        });
    }

    private async System.Threading.Tasks.Task LoadProjectsAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var projects = await db.Projects.OrderBy(p => p.Name).ToListAsync();
        ProjectCombo.ItemsSource = projects;
        if (projects.Count > 0)
        {
            ProjectCombo.SelectedIndex = 0;
            await LoadTasksForProjectAsync((Guid)ProjectCombo.SelectedValue!);
        }
    }

    private async System.Threading.Tasks.Task LoadProjectTasksAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(projectId);
        ProjectNameBox.Text = project?.Name ?? "—";
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).OrderBy(t => t.Name).ToListAsync();
        TaskCombo.ItemsSource = tasks;
        if (tasks.Count > 0)
        {
            TaskCombo.SelectedIndex = 0;
            await LoadAssigneesFromTaskAsync((Guid)TaskCombo.SelectedValue!);
        }
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectCombo.SelectedValue is Guid projectId)
            await LoadTasksForProjectAsync(projectId);
    }

    private async void TaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskCombo.SelectedValue is Guid taskId)
        {
            _selectedAssigneeIds.Clear();
            await LoadAssigneesFromTaskAsync(taskId);
        }
    }

    private async System.Threading.Tasks.Task LoadTasksForProjectAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).OrderBy(t => t.Name).ToListAsync();
        TaskCombo.ItemsSource = tasks;
        if (tasks.Count > 0)
        {
            TaskCombo.SelectedIndex = 0;
            await LoadAssigneesFromTaskAsync((Guid)TaskCombo.SelectedValue!);
        }
    }

    private void RefreshAssigneeItems()
    {
        foreach (var item in _allAssigneeItems)
            item.RefreshSelected(_selectedAssigneeIds);
        AssigneePickerList.ItemsSource = null;
        AssigneePickerList.ItemsSource = _allAssigneeItems;
    }

    private void RefreshAssigneeChips()
    {
        SelectedAssigneesPanel.Children.Clear();
        var selected = _allAssigneeItems.Where(i => _selectedAssigneeIds.Contains(i.UserId)).ToList();
        SelectedAssigneesPanel.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in selected)
            SelectedAssigneesPanel.Children.Add(BuildChip(item));
    }

    private Border BuildChip(AssigneePickerItem item)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
            BorderThickness = new Thickness(1)
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var avatar = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = item.AvatarBrush,
            Margin = new Thickness(0, 0, 5, 0),
            ClipToBounds = true
        };
        if (!string.IsNullOrEmpty(item.AvatarPath) && System.IO.File.Exists(item.AvatarPath))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(item.AvatarPath, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                avatar.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill, Width = 20, Height = 20 };
                avatar.Background = Brushes.Transparent;
            }
            catch { avatar.Child = CreateStageInitialsBlock(item.Initials); }
        }
        else
        {
            avatar.Child = CreateStageInitialsBlock(item.Initials);
        }
        sp.Children.Add(avatar);
        sp.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var removeBtn = new Button
        {
            Content = new TextBlock { Text = "✕", FontSize = 9, Foreground = Brushes.Gray },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand, Margin = new Thickness(4, 0, 0, 0),
            Tag = item.UserId
        };
        removeBtn.Click += (s, _) =>
        {
            if (s is Button b && b.Tag is Guid uid)
            {
                _selectedAssigneeIds.Remove(uid);
                RefreshAssigneeItems();
                RefreshAssigneeChips();
            }
        };
        sp.Children.Add(removeBtn);
        chip.Child = sp;
        return chip;
    }

    private static TextBlock CreateStageInitialsBlock(string initials) => new()
    {
        Text = initials, FontSize = 7, FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private void AssigneeItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;
        if (_selectedAssigneeIds.Contains(item.UserId))
            _selectedAssigneeIds.Remove(item.UserId);
        else
            _selectedAssigneeIds.Add(item.UserId);
        RefreshAssigneeItems();
        RefreshAssigneeChips();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_onAfterSave is not null)
            _onAfterSave();
        else
            MainWindow.Instance?.HideDrawer();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название этапа."); return; }

        if (_editStage is null && _selectedAssigneeIds.Count == 0)
        { ShowError("Назначьте хотя бы одного исполнителя на этап."); return; }

        Guid taskId;
        TaskDetailViewModel? vm;

        if (ProjectTaskPickerRow.Visibility == Visibility.Visible)
        {
            if (TaskCombo.SelectedValue is not Guid tid)
            { ShowError("Выберите задачу."); return; }
            taskId = tid;
            vm = App.Services.GetRequiredService<TaskDetailViewModel>();
            var task = await GetTaskByIdAsync(taskId);
            if (task is null) { ShowError("Задача не найдена."); return; }
            vm.SetTask(task);
        }
        else
        {
            if (_task is null || _vm is null)
            { ShowError("Ошибка: задача не установлена."); return; }
            taskId = _task.Id;
            vm = _vm;
        }

        var primaryAssigneeId = _selectedAssigneeIds.Count > 0
            ? _selectedAssigneeIds.First()
            : (Guid?)null;

        SaveButton.IsEnabled = false;
        try
        {
            Guid stageId;
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();

            if (_editStage is null)
            {
                var localId = Guid.NewGuid();
                var req = new CreateStageRequest(
                    taskId,
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId);
                await vm.SaveNewStageAsync(req, localId);
                stageId = localId;
            }
            else
            {
                var status = GetStatus();
                var req = new UpdateStageRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId, status);
                await vm.SaveUpdatedStageAsync(_editStage.Id, req);
                stageId = _editStage.Id;
            }

            // Save multi-assignees for stage
            await SaveStageAssigneesAsync(stageId, dbFactory);

            if (_onSaved is not null) await _onSaved();

            if (_onAfterSave is not null)
                _onAfterSave();
            else
                MainWindow.Instance?.HideDrawer();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task SaveStageAssigneesAsync(
        Guid stageId, IDbContextFactory<LocalDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Remove all existing stage assignees
        var existing = await db.StageAssignees.Where(a => a.StageId == stageId).ToListAsync();
        db.StageAssignees.RemoveRange(existing);

        // Add selected (only from task assignees)
        foreach (var uid in _selectedAssigneeIds)
        {
            var item = _allAssigneeItems.FirstOrDefault(i => i.UserId == uid);
            if (item is null) continue;
            db.StageAssignees.Add(new LocalStageAssignee
            {
                Id = Guid.NewGuid(),
                StageId = stageId,
                UserId = uid,
                UserName = item.Name
            });
        }
        await db.SaveChangesAsync();
    }

    private void StatusPlanned_Click(object sender, RoutedEventArgs e)
    {
        _selectedStatus = StageStatus.Planned;
        ApplyStatusSelection(_selectedStatus);
    }

    private void StatusInProgress_Click(object sender, RoutedEventArgs e)
    {
        _selectedStatus = StageStatus.InProgress;
        ApplyStatusSelection(_selectedStatus);
    }

    private void StatusCompleted_Click(object sender, RoutedEventArgs e)
    {
        _selectedStatus = StageStatus.Completed;
        ApplyStatusSelection(_selectedStatus);
    }

    private void ApplyStatusSelection(StageStatus status)
    {
        var neutral = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
        var neutralBg = new SolidColorBrush(Colors.White);
        var neutralFg = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));

        BtnStatusPlanned.BorderBrush = status == StageStatus.Planned
            ? new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x4D)) : neutral;
        BtnStatusPlanned.Background = status == StageStatus.Planned
            ? new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)) : neutralBg;
        BtnStatusPlanned.Foreground = status == StageStatus.Planned
            ? new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x4D)) : neutralFg;
        BtnStatusPlanned.FontWeight = status == StageStatus.Planned ? FontWeights.SemiBold : FontWeights.Normal;

        BtnStatusInProgress.BorderBrush = status == StageStatus.InProgress
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)) : neutral;
        BtnStatusInProgress.Background = status == StageStatus.InProgress
            ? new SolidColorBrush(Color.FromRgb(0xEB, 0xF2, 0xFF)) : neutralBg;
        BtnStatusInProgress.Foreground = status == StageStatus.InProgress
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2)) : neutralFg;
        BtnStatusInProgress.FontWeight = status == StageStatus.InProgress ? FontWeights.SemiBold : FontWeights.Normal;

        BtnStatusCompleted.BorderBrush = status == StageStatus.Completed
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutral;
        BtnStatusCompleted.Background = status == StageStatus.Completed
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)) : neutralBg;
        BtnStatusCompleted.Foreground = status == StageStatus.Completed
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutralFg;
        BtnStatusCompleted.FontWeight = status == StageStatus.Completed ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private StageStatus GetStatus() => _selectedStatus;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private async System.Threading.Tasks.Task<LocalTask?> GetTaskByIdAsync(Guid taskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tasks.FindAsync(taskId);
    }
}
