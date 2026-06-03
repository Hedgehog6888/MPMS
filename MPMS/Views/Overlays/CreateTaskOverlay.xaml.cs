using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class CreateTaskOverlay : UserControl
{
    private readonly DispatcherTimer _errorHideTimer;
    private TasksViewModel? _tasksVm;
    private LocalTask? _editTask;
    private Guid? _fixedProjectId;
    private Func<System.Threading.Tasks.Task>? _onSaved;
    private Action? _onAfterSave;
    private List<AssigneePickerItem> _allAssigneeItems = [];
    private List<AssigneePickerItem> _foremanItems = [];
    private List<AssigneePickerItem> _workerItems = [];
    private readonly HashSet<Guid> _selectedAssigneeIds = [];
    private TaskPriority _selectedPriority = TaskPriority.Medium;
    private bool _isCurrentUserForeman = false;
    private Guid? _currentUserId = null;

    public CreateTaskOverlay()
    {
        InitializeComponent();
        _errorHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _errorHideTimer.Tick += (_, _) =>
        {
            _errorHideTimer.Stop();
            ErrorPanel.Visibility = Visibility.Collapsed;
        };
        DueDatePickerRestrictions.AttachNoPastSelectableBlackout(DueDatePicker);
        Loaded += (_, _) => ApplyPrioritySelection(_selectedPriority);
    }

    public void SetCreateMode(TasksViewModel vm, Guid? projectId = null,
        Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _tasksVm = vm;
        _fixedProjectId = projectId;
        _onSaved = onSaved;
        TitleLabel.Text = "Создать задачу";
        SubtitleLabel.Text = "Добавьте задачу для отслеживания выполнения";
        SaveButton.Content = "Создать задачу";
        _ = LoadDataAsync(null, null);
    }

    public void SetEditMode(LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null, Action? onAfterSave = null)
    {
        _editTask = task;
        _onSaved = onSaved;
        _onAfterSave = onAfterSave;
        TitleLabel.Text = "Редактировать задачу";
        SubtitleLabel.Text = "Измените название, описание, приоритет и исполнителей";
        SaveButton.Content = "Сохранить изменения";
        StatusRow.Visibility = Visibility.Collapsed;

        NameBox.Text = task.Name;
        DescriptionBox.Text = task.Description ?? "";
        _ = LoadDataAsync(task.ProjectId, task.Id);
    }

    private async System.Threading.Tasks.Task LoadDataAsync(Guid? preselectedProjectId, Guid? editTaskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var authService = App.Services.GetRequiredService<IAuthService>();
        var currentUserId = authService.UserId;

        var projectQuery = db.Projects
            .Where(p => !p.IsArchived && !p.IsMarkedForDeletion && !p.IsClosed);

        if (currentUserId.HasValue)
        {
            var currentUserRole = authService.UserRole ?? "";
            bool isManager = string.Equals(currentUserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentUserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentUserRole, "Manager", StringComparison.OrdinalIgnoreCase);
            bool isForeman = string.Equals(currentUserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
            bool isWorker = string.Equals(currentUserRole, "Worker", StringComparison.OrdinalIgnoreCase);

            if (isManager)
            {
                projectQuery = projectQuery.Where(p => p.ManagerId == currentUserId.Value);
            }
            else if (isForeman)
            {
                var assignedProjectIds = await db.ProjectMembers
                    .Where(m => m.UserId == currentUserId.Value)
                    .Select(m => m.ProjectId)
                    .ToListAsync();
                projectQuery = projectQuery.Where(p => assignedProjectIds.Contains(p.Id));
            }
            else if (isWorker)
            {
                var projectIdsFromTaskAssignee = await db.Tasks
                    .Where(t => t.AssignedUserId == currentUserId.Value)
                    .Select(t => t.ProjectId)
                    .ToListAsync();
                var projectIdsFromTaskAssignees = await db.TaskAssignees
                    .Where(ta => ta.UserId == currentUserId.Value)
                    .Join(db.Tasks, ta => ta.TaskId, t => t.Id, (_, t) => t.ProjectId)
                    .ToListAsync();
                var projectIdsFromStageAssignees = await db.StageAssignees
                    .Where(sa => sa.UserId == currentUserId.Value)
                    .Join(db.TaskStages, sa => sa.StageId, s => s.Id, (_, s) => s.TaskId)
                    .Join(db.Tasks, tid => tid, t => t.Id, (_, t) => t.ProjectId)
                    .ToListAsync();
                var projectIdsFromStageAssigned = await db.TaskStages
                    .Where(s => s.AssignedUserId == currentUserId.Value)
                    .Join(db.Tasks, s => s.TaskId, t => t.Id, (_, t) => t.ProjectId)
                    .ToListAsync();
                var workerProjectIds = projectIdsFromTaskAssignee
                    .Concat(projectIdsFromTaskAssignees)
                    .Concat(projectIdsFromStageAssignees)
                    .Concat(projectIdsFromStageAssigned)
                    .Distinct()
                    .ToList();
                projectQuery = projectQuery.Where(p => workerProjectIds.Contains(p.Id));
            }
        }

        var projects = await projectQuery.OrderBy(p => p.Name).ToListAsync();
        ProjectCombo.ItemsSource = projects;

        if (_fixedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = _fixedProjectId.Value;
            ProjectCombo.IsEnabled = false;
            await LoadAssigneesForProjectAsync(_fixedProjectId.Value);
        }
        else if (preselectedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = preselectedProjectId.Value;
            await LoadAssigneesForProjectAsync(preselectedProjectId.Value);
        }
        if (editTaskId.HasValue)
            ProjectCombo.IsEnabled = false;

        if (editTaskId.HasValue && _editTask is not null)
        {
            _selectedPriority = _editTask.Priority;
            ApplyPrioritySelection(_selectedPriority);

            foreach (ComboBoxItem item in StatusCombo.Items)
                if (item.Tag?.ToString() == _editTask.Status.ToString())
                { StatusCombo.SelectedItem = item; break; }

            if (_editTask.DueDate.HasValue)
                DueDatePicker.SelectedDate = _editTask.DueDate.Value.ToDateTime(TimeOnly.MinValue);

            // Load existing task assignees (exclude blocked users)
            var blockedIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
            var taskAssignees = await db.TaskAssignees
                .Where(ta => ta.TaskId == _editTask.Id && !blockedIds.Contains(ta.UserId))
                .ToListAsync();
            foreach (var ta in taskAssignees)
                _selectedAssigneeIds.Add(ta.UserId);

            if (_editTask.AssignedUserId.HasValue && !blockedIds.Contains(_editTask.AssignedUserId.Value)
                && !_selectedAssigneeIds.Contains(_editTask.AssignedUserId.Value))
                _selectedAssigneeIds.Add(_editTask.AssignedUserId.Value);

            RefreshAssigneeItems();
            RefreshAssigneeChips();
        }
    }

    private async System.Threading.Tasks.Task LoadAssigneesForProjectAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var authService = App.Services.GetRequiredService<IAuthService>();
        _currentUserId = authService.UserId;
        var currentUserRole = authService.UserRole ?? "";

        _isCurrentUserForeman = currentUserRole is "Foreman" or "Прораб";
        var isCurrentUserManager = currentUserRole is "ProjectManager" or "Manager" or "Project Manager";

        var blockedUserIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
        var members = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && !blockedUserIds.Contains(m.UserId))
            .OrderBy(m => m.UserName)
            .ToListAsync();

        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var userRows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarPath, u.AvatarData, u.SubRole, u.AdditionalSubRoles })
            .ToDictionaryAsync(u => u.Id);
        foreach (var m in members)
        {
            if (userRows.TryGetValue(m.UserId, out var ur))
            {
                m.AvatarPath = ur.AvatarPath;
                m.AvatarData = ur.AvatarData;
            }
        }

        AssigneePickerItem BuildItem(LocalProjectMember m)
        {
            userRows.TryGetValue(m.UserId, out var ur);
            return new AssigneePickerItem(
                m.UserId, m.UserName, m.UserRole, _selectedAssigneeIds,
                m.AvatarPath, m.AvatarData, ur?.SubRole, ur?.AdditionalSubRoles);
        }

        _foremanItems = members
            .Where(m => m.UserRole is "Foreman" or "Прораб")
            .Select(BuildItem)
            .ToList();

        if (_currentUserId.HasValue)
        {
            var currentUserInForemen = _foremanItems.FirstOrDefault(item => item.UserId == _currentUserId.Value);
            if (currentUserInForemen != null)
            {
                _foremanItems = [currentUserInForemen];
                // Auto-select the current user
                if (!_selectedAssigneeIds.Contains(_currentUserId.Value))
                    _selectedAssigneeIds.Add(_currentUserId.Value);
            }
        }

        _workerItems = members
            .Where(m => m.UserRole is "Worker" or "Работник")
            .Select(BuildItem)
            .ToList();

        if (_currentUserId.HasValue)
        {
            var currentUserInWorkers = _workerItems.FirstOrDefault(item => item.UserId == _currentUserId.Value);
            if (currentUserInWorkers != null)
            {
                _workerItems = [currentUserInWorkers];
                if (!_selectedAssigneeIds.Contains(_currentUserId.Value))
                    _selectedAssigneeIds.Add(_currentUserId.Value);
            }
        }

        _allAssigneeItems = [.. _foremanItems, .. _workerItems];

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_allAssigneeItems.Count == 0)
            {
                NoProjHint.Visibility = Visibility.Visible;
                NoProjHintText.Text = "В проекте нет назначенных прорабов или работников. Добавьте команду в проект.";
                ForemanPickerBorder.Visibility = Visibility.Collapsed;
                WorkerPickerBorder.Visibility = Visibility.Collapsed;
                ForemanSectionTitle.Visibility = Visibility.Collapsed;
                WorkerSectionTitle.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoProjHint.Visibility = Visibility.Collapsed;
                ForemanPickerBorder.Visibility = _foremanItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ForemanSectionTitle.Visibility = _foremanItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                WorkerPickerBorder.Visibility = _workerItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                WorkerSectionTitle.Visibility = _workerItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshAssigneeItems();
        });
    }

    private void RefreshAssigneeItems()
    {
        foreach (var item in _foremanItems)
            item.RefreshSelected(_selectedAssigneeIds);
        foreach (var item in _workerItems)
            item.RefreshSelected(_selectedAssigneeIds);

        ForemanPickerList.ItemsSource = null;
        ForemanPickerList.ItemsSource = _foremanItems;
        WorkerPickerList.ItemsSource = null;
        WorkerPickerList.ItemsSource = _workerItems;

        NoForemenHint.Visibility = _foremanItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        NoWorkersHint.Visibility = _workerItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAssigneeChips()
    {
    }

    private void ForemanItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;
        if (item.UserId == _currentUserId)
            return;
        if (_selectedAssigneeIds.Contains(item.UserId))
            _selectedAssigneeIds.Remove(item.UserId);
        else
            _selectedAssigneeIds.Add(item.UserId);
        RefreshAssigneeItems();
        RefreshAssigneeChips();
    }

    private void WorkerItem_Click(object sender, MouseButtonEventArgs e)
        => ForemanItem_Click(sender, e);

    private void NestedScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer nested)
            return;

        var atTop = nested.VerticalOffset <= 0;
        var atBottom = nested.VerticalOffset >= nested.ScrollableHeight;
        var scrollingUp = e.Delta > 0;
        var scrollingDown = e.Delta < 0;

        if ((atTop && scrollingUp) || (atBottom && scrollingDown))
        {
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectCombo.SelectedValue is Guid projectId)
        {
            _selectedAssigneeIds.Clear();
            await LoadAssigneesForProjectAsync(projectId);
        }
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
        _errorHideTimer.Stop();
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название задачи"); return; }
        if (ProjectCombo.SelectedValue is not Guid projectId)
        { ShowError("Выберите проект"); return; }
        if (DueDatePicker.SelectedDate is null)
        { ShowError("Выберите срок выполнения"); return; }

        if (_selectedAssigneeIds.Count == 0)
        { ShowError("Назначьте хотя бы одного исполнителя на задачу"); return; }

        var priority = GetPriority();
        var dueDate = DateOnly.FromDateTime(DueDatePicker.SelectedDate.Value);

        if (_editTask is null || dueDate != _editTask.DueDate)
        {
            if (!DueDatePolicy.IsAllowed(dueDate))
            { ShowError(DueDatePolicy.PastNotAllowedMessage); return; }
        }

        var primaryAssigneeId = _selectedAssigneeIds.Count > 0
            ? _selectedAssigneeIds.First()
            : (Guid?)null;

        SaveButton.IsEnabled = false;
        try
        {
            Guid taskId;
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();

            if (_editTask is null)
            {
                var tasksVm = _tasksVm ?? App.Services.GetRequiredService<TasksViewModel>();
                var localId = Guid.NewGuid();
                var req = new CreateTaskRequest(
                    projectId,
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId, priority, dueDate);
                await tasksVm.SaveNewTaskAsync(req, localId);
                if (_onSaved is not null) await _onSaved();
                taskId = localId;
            }
            else
            {
                var status = GetStatus();
                var taskDetailVm = App.Services.GetRequiredService<MPMS.ViewModels.TaskDetailViewModel>();
                var req = new UpdateTaskRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId, priority, dueDate, status,
                    _editTask.IsMarkedForDeletion, _editTask.IsArchived);
                await taskDetailVm.EditTaskAsync(_editTask.Id, req, skipAssigneeLogging: true);
                if (_onSaved is not null) await _onSaved();
                taskId = _editTask.Id;
            }

            await SaveTaskAssigneesAsync(taskId, dbFactory);

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

    private async System.Threading.Tasks.Task SaveTaskAssigneesAsync(
        Guid taskId, IDbContextFactory<LocalDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var existing = await db.TaskAssignees.Where(a => a.TaskId == taskId).ToListAsync();
        var existingById = existing.ToDictionary(x => x.UserId, x => x.UserName);
        var newById = _selectedAssigneeIds.ToDictionary(uid => uid, uid => _allAssigneeItems.FirstOrDefault(i => i.UserId == uid)?.Name ?? "неизвестный");

        var added = newById.Where(kvp => !existingById.ContainsKey(kvp.Key)).Select(kvp => kvp.Value).ToList();
        var removed = existingById.Where(kvp => !newById.ContainsKey(kvp.Key)).Select(kvp => kvp.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        db.TaskAssignees.RemoveRange(existing);

        foreach (var uid in _selectedAssigneeIds)
        {
            var item = _allAssigneeItems.FirstOrDefault(i => i.UserId == uid);
            if (item is null) continue;
            db.TaskAssignees.Add(new LocalTaskAssignee
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                UserId = uid,
                UserName = item.Name
            });
        }
        await db.SaveChangesAsync();

        var sync = App.Services.GetRequiredService<ISyncService>();
        var rows = await db.TaskAssignees.Where(a => a.TaskId == taskId).ToListAsync();
        await sync.QueueOperationAsync("TaskAssignees", taskId, SyncOperation.Update,
            new ReplaceTaskAssigneesRequest(rows.Select(a => new AssigneeSyncItemDto(a.Id, a.UserId)).ToList()));

        if (added.Count > 0 || removed.Count > 0)
        {
            var parts = new List<string>();
            if (added.Count > 0)
                parts.Add($"Добавлены исполнители: {string.Join(", ", added)}");
            if (removed.Count > 0)
                parts.Add($"Исключены исполнители: {string.Join(", ", removed)}");

            var auth = App.Services.GetRequiredService<IAuthService>();
            var userName = auth.UserName ?? "Система";
            var userId = auth.UserId;
            var userRole = auth.UserRole;

            db.ActivityLogs.Add(new LocalActivityLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ActorRole = userRole,
                ActionType = ActivityActionKind.Updated,
                ActionText = $"Задача: {string.Join("; ", parts)}",
                EntityType = "Task",
                EntityId = taskId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }

    private void PriorityLow_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.Low;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void PriorityMedium_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.Medium;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void PriorityHigh_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.High;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void PriorityCritical_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.Critical;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void ApplyPrioritySelection(TaskPriority priority)
    {
        var neutral = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
        var neutralBg = new SolidColorBrush(Colors.White);
        var neutralFg = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));

        BtnPriorityLow.BorderBrush = priority == TaskPriority.Low
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutral;
        BtnPriorityLow.Background = priority == TaskPriority.Low
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)) : neutralBg;
        BtnPriorityLow.Foreground = priority == TaskPriority.Low
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutralFg;
        BtnPriorityLow.FontWeight = priority == TaskPriority.Low ? FontWeights.SemiBold : FontWeights.Normal;

        BtnPriorityMedium.BorderBrush = priority == TaskPriority.Medium
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)) : neutral;
        BtnPriorityMedium.Background = priority == TaskPriority.Medium
            ? new SolidColorBrush(Color.FromRgb(0xEB, 0xF2, 0xFF)) : neutralBg;
        BtnPriorityMedium.Foreground = priority == TaskPriority.Medium
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2)) : neutralFg;
        BtnPriorityMedium.FontWeight = priority == TaskPriority.Medium ? FontWeights.SemiBold : FontWeights.Normal;

        BtnPriorityHigh.BorderBrush = priority == TaskPriority.High
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x00)) : neutral;
        BtnPriorityHigh.Background = priority == TaskPriority.High
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE6)) : neutralBg;
        BtnPriorityHigh.Foreground = priority == TaskPriority.High
            ? new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)) : neutralFg;
        BtnPriorityHigh.FontWeight = priority == TaskPriority.High ? FontWeights.SemiBold : FontWeights.Normal;

        BtnPriorityCritical.BorderBrush = priority == TaskPriority.Critical
            ? new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B)) : neutral;
        BtnPriorityCritical.Background = priority == TaskPriority.Critical
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xE6)) : neutralBg;
        BtnPriorityCritical.Foreground = priority == TaskPriority.Critical
            ? new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B)) : neutralFg;
        BtnPriorityCritical.FontWeight = priority == TaskPriority.Critical ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private TaskPriority GetPriority() => _selectedPriority;

    private TaskStatus GetStatus()
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "InProgress" => TaskStatus.InProgress,
                "Paused" => TaskStatus.Paused,
                "Completed" => TaskStatus.Completed,
                _ => TaskStatus.Planned
            };
        }
        return TaskStatus.Planned;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        _errorHideTimer.Stop();
        _errorHideTimer.Start();
    }
}
public sealed class AssigneePickerItem : INotifyPropertyChanged
{
    public Guid UserId { get; }
    public string Name { get; }
    public string? AvatarPath { get; }
    public byte[]? AvatarData { get; }
    public string RoleDisplay { get; }
    public string? RoleSubtitle { get; }
    public Visibility RoleSubtitleVis =>
        string.IsNullOrWhiteSpace(RoleSubtitle) ? Visibility.Collapsed : Visibility.Visible;
    public SolidColorBrush RoleSubtitleBrush { get; }
    public string Initials { get; }
    public SolidColorBrush AvatarBrush { get; }
    public SolidColorBrush RoleColorBrush { get; }
    public string RoleColor => RoleColorBrush.Color.ToString();
    public bool IsForemanPicker =>
        RoleDisplay == "Прораб";

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

    public AssigneePickerItem(
        Guid userId,
        string name,
        string role,
        HashSet<Guid> selectedIds,
        string? avatarPath = null,
        byte[]? avatarData = null,
        string? subRole = null,
        string? additionalSubRolesJson = null)
    {
        UserId = userId;
        Name = name;
        AvatarPath = avatarPath;
        AvatarData = avatarData;
        _isSelected = selectedIds.Contains(userId);

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : name.Length > 0 ? name[0].ToString().ToUpper() : "?";

        (RoleDisplay, AvatarBrush, RoleColorBrush, RoleSubtitle, RoleSubtitleBrush) = role switch
        {
            "Foreman" or "Прораб" => (
                "Прораб",
                new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)),
                new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)),
                null,
                new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E))),
            "Worker" or "Работник" => BuildWorkerPickerBrushes(subRole, additionalSubRolesJson),
            _ => (
                role,
                new SolidColorBrush(Colors.Black),
                new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)),
                null,
                new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)))
        };
    }

    private static (string RoleDisplay, SolidColorBrush AvatarBrush, SolidColorBrush RoleColorBrush, string? RoleSubtitle, SolidColorBrush RoleSubtitleBrush) BuildWorkerPickerBrushes(
        string? subRole, string? additionalSubRolesJson)
    {
        var specKey = WorkerSpecialtiesJson.PrimaryDisplaySpecForColor(subRole, additionalSubRolesJson);
        var av = WorkerSpecialtiesJson.PickerAvatarRgbForSpecName(specKey);
        var fg = WorkerSpecialtiesJson.BadgeForegroundRgbForSpecName(specKey);
        var avatarBrush = new SolidColorBrush(Color.FromRgb(av.R, av.G, av.B));
        var lineBrush = new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
        return (
            "Работник",
            avatarBrush,
            lineBrush,
            WorkerSpecialtiesJson.FormatWorkerLineCompact(subRole, additionalSubRolesJson),
            lineBrush);
    }

    public void RefreshSelected(HashSet<Guid> selectedIds)
    {
        IsSelected = selectedIds.Contains(UserId);
    }
}
