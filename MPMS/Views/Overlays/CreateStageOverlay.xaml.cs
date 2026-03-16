using System.Collections.Generic;
using System.ComponentModel;
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
    private List<StageSelectionItem> _projectItems = [];
    private List<StageSelectionItem> _taskItems = [];
    private readonly HashSet<Guid> _selectedAssigneeIds = [];
    private bool _isWorkerMode; // работник не выбирает исполнителей — автоматом он сам

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
        ProjectPickerSection.Visibility = Visibility.Visible;
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
        ProjectPickerSection.Visibility = Visibility.Collapsed;
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
        TaskNameLabel.Text = $"Задача: {task.Name}";

        NameBox.Text = stage.Name;
        DescriptionBox.Text = stage.Description ?? "";

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

        // Get task assignees (people who can be assigned to stages of this task), exclude blocked users
        var blockedUserIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
        var taskAssignees = await db.TaskAssignees
            .Where(ta => ta.TaskId == taskId && !blockedUserIds.Contains(ta.UserId))
            .OrderBy(ta => ta.UserName)
            .ToListAsync();

        // Also check legacy single-assignee on the task entity (if not blocked)
        LocalTask? taskEntity = await db.Tasks.FindAsync(taskId);
        if (taskAssignees.Count == 0 && taskEntity?.AssignedUserId.HasValue == true)
        {
            var legacyId = taskEntity.AssignedUserId!.Value;
            if (!blockedUserIds.Contains(legacyId))
            {
                taskAssignees.Add(new LocalTaskAssignee
                {
                    TaskId = taskId,
                    UserId = legacyId,
                    UserName = taskEntity.AssignedUserName ?? "—"
                });
            }
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

        // Populate AvatarData/AvatarPath from Users
        var userIds = taskAssignees.Select(ta => ta.UserId).Distinct().ToList();
        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarPath, u.AvatarData })
            .ToDictionaryAsync(u => u.Id);
        foreach (var ta in taskAssignees)
        {
            if (userAvatars.TryGetValue(ta.UserId, out var av))
            {
                ta.AvatarPath = av.AvatarPath;
                ta.AvatarData = av.AvatarData;
            }
        }

        // Only task assignees (project members assigned to task) can be assigned to stages
        if (taskAssignees.Count == 0)
        {
            _allAssigneeItems = [];
        }
        else
        {
            _allAssigneeItems = taskAssignees.Select(ta => new AssigneePickerItem(
                ta.UserId, ta.UserName, "Worker", _selectedAssigneeIds, ta.AvatarPath, ta.AvatarData)).ToList();
        }

        // Load existing stage assignees if editing (exclude blocked users)
        if (stageId.HasValue)
        {
            var stageAssignees = await db.StageAssignees
                .Where(sa => sa.StageId == stageId.Value && !blockedUserIds.Contains(sa.UserId))
                .ToListAsync();
            foreach (var sa in stageAssignees)
                _selectedAssigneeIds.Add(sa.UserId);

            // Also check legacy single assignee (if not blocked)
            var stageEntity = await db.TaskStages.FindAsync(stageId.Value);
            if (stageEntity?.AssignedUserId.HasValue == true && !blockedUserIds.Contains(stageEntity.AssignedUserId!.Value)
                && !_selectedAssigneeIds.Contains(stageEntity.AssignedUserId!.Value))
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
        _projectItems = projects.Select(p => new StageSelectionItem(p.Id, p.Name)).ToList();
        ProjectPickerList.ItemsSource = _projectItems;
        NoProjectsHint.Visibility = _projectItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (projects.Count > 0)
        {
            ProjectCombo.SelectedIndex = 0;
            RefreshProjectItems();
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
        _taskItems = tasks.Select(t => new StageSelectionItem(t.Id, t.Name)).ToList();
        TaskPickerList.ItemsSource = _taskItems;
        NoTasksHint.Visibility = _taskItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tasks.Count > 0)
        {
            TaskCombo.SelectedIndex = 0;
            RefreshTaskItems();
        }
        else
        {
            _allAssigneeItems = [];
            RefreshAssigneeItems();
            RefreshAssigneeChips();
        }
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectCombo.SelectedValue is Guid projectId)
        {
            RefreshProjectItems();
            await LoadTasksForProjectAsync(projectId);
        }
    }

    private async void TaskCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TaskCombo.SelectedValue is Guid taskId)
        {
            RefreshTaskItems();
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
        _taskItems = tasks.Select(t => new StageSelectionItem(t.Id, t.Name)).ToList();
        TaskPickerList.ItemsSource = _taskItems;
        NoTasksHint.Visibility = _taskItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tasks.Count > 0)
        {
            TaskCombo.SelectedIndex = 0;
            RefreshTaskItems();
        }
        else
        {
            _allAssigneeItems = [];
            RefreshAssigneeItems();
            RefreshAssigneeChips();
        }
    }

    private void ProjectItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is StageSelectionItem item)
            ProjectCombo.SelectedValue = item.Id;
    }

    private void TaskItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is StageSelectionItem item)
            TaskCombo.SelectedValue = item.Id;
    }

    private void RefreshProjectItems()
    {
        var selectedId = ProjectCombo.SelectedValue is Guid id ? id : (Guid?)null;
        foreach (var item in _projectItems)
            item.RefreshSelected(selectedId);
        ProjectPickerList.ItemsSource = null;
        ProjectPickerList.ItemsSource = _projectItems;
    }

    private void RefreshTaskItems()
    {
        var selectedId = TaskCombo.SelectedValue is Guid id ? id : (Guid?)null;
        foreach (var item in _taskItems)
            item.RefreshSelected(selectedId);
        TaskPickerList.ItemsSource = null;
        TaskPickerList.ItemsSource = _taskItems;
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
        sp.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var removeBtn = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipRemoveButton"),
            Content = new TextBlock { Text = "✕", FontSize = 9, Foreground = Brushes.Gray },
            Margin = new Thickness(4, 0, 0, 0),
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
                var req = new UpdateStageRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId, _editStage.Status);
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

public sealed class StageSelectionItem : INotifyPropertyChanged
{
    public Guid Id { get; }
    public string Name { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedVis)));
        }
    }

    public Visibility IsSelectedVis => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StageSelectionItem(Guid id, string name, bool isSelected = false)
    {
        Id = id;
        Name = name;
        _isSelected = isSelected;
    }

    public void RefreshSelected(Guid? selectedId)
    {
        IsSelected = selectedId == Id;
    }
}
