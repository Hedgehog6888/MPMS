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
        _ = LoadAssigneesFromTaskAsync(task.Id);
    }

    public void SetCreateModeFromStagesPage(Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = null;
        _vm = null;
        _editStage = null;
        _onSaved = onSaved;
        TitleLabel.Text = "Добавить этап";
        SaveButton.Content = "Добавить этап";
        TaskNameLabel.Text = "Выберите проект и задачу";
        ProjectTaskPickerRow.Visibility = Visibility.Visible;
        StatusRow.Visibility = Visibility.Collapsed;
        _ = LoadProjectsAsync();
    }

    public void SetCreateModeForProject(Guid projectId, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = null;
        _vm = null;
        _editStage = null;
        _onSaved = onSaved;
        TitleLabel.Text = "Добавить этап";
        SaveButton.Content = "Добавить этап";
        TaskNameLabel.Text = "Выберите задачу";
        ProjectTaskPickerRow.Visibility = Visibility.Visible;
        StatusRow.Visibility = Visibility.Collapsed;
        ProjectCombo.Visibility = Visibility.Collapsed;
        ProjectNameRow.Visibility = Visibility.Visible;
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
        TitleLabel.Text = "Редактировать этап";
        SaveButton.Content = "Сохранить";
        StatusRow.Visibility = Visibility.Visible;
        TaskNameLabel.Text = $"Задача: {task.Name}";

        NameBox.Text = stage.Name;
        DescriptionBox.Text = stage.Description ?? "";

        foreach (ComboBoxItem item in StatusCombo.Items)
            if (item.Tag?.ToString() == stage.Status.ToString())
            { StatusCombo.SelectedItem = item; break; }

        _ = LoadAssigneesFromTaskAsync(task.Id, stage.Id);
    }

    private async System.Threading.Tasks.Task LoadAssigneesFromTaskAsync(Guid taskId, Guid? stageId = null)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
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

        // If still empty: fallback to all worker-role users
        if (taskAssignees.Count == 0)
        {
            var workerRoles = new[] { "Foreman", "Прораб", "Worker", "Работник" };
            var users = await db.Users.Where(u => workerRoles.Contains(u.RoleName)).ToListAsync();
            _allAssigneeItems = users.Select(u => new AssigneePickerItem(
                u.Id, u.Name, u.RoleName, _selectedAssigneeIds)).ToList();
        }
        else
        {
            _allAssigneeItems = taskAssignees.Select(ta => new AssigneePickerItem(
                ta.UserId, ta.UserName, "Worker", _selectedAssigneeIds)).ToList();
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
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
            BorderThickness = new Thickness(1)
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var avatar = new Border
        {
            Width = 18, Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = item.AvatarBrush,
            Margin = new Thickness(0, 0, 5, 0)
        };
        avatar.Child = new TextBlock
        {
            Text = item.Initials, FontSize = 7, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
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

    private StageStatus GetStatus()
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "InProgress" => StageStatus.InProgress,
                "Completed"  => StageStatus.Completed,
                _            => StageStatus.Planned
            };
        }
        return StageStatus.Planned;
    }

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
