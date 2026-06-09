using System;
using System.Collections.Generic;
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

namespace MPMS.Views.Overlays;

public partial class CreateStageOverlay : UserControl
{
    private readonly DispatcherTimer _errorHideTimer;
    private LocalTaskStage? _editStage;
    private LocalTask? _task;
    private Guid? _fixedTaskId;
    private Guid? _fixedProjectId;
    private Func<System.Threading.Tasks.Task>? _onSaved;
    private Action? _onAfterSave;
    private List<AssigneePickerItem> _foremanItems = [];
    private List<AssigneePickerItem> _workerItems = [];
    private readonly HashSet<Guid> _selectedAssigneeIds = [];

    public CreateStageOverlay()
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
    }

    public void SetCreateMode(Guid? fixedTaskId = null, Guid? fixedProjectId = null,
        Func<System.Threading.Tasks.Task>? onSaved = null, Action? onAfterSave = null)
    {
        _editStage = null;
        _fixedTaskId = fixedTaskId;
        _fixedProjectId = fixedProjectId;
        _onSaved = onSaved;
        _onAfterSave = onAfterSave;
        TitleLabel.Text = "Создать этап";
        SubtitleLabel.Text = "Добавьте этап для отслеживания выполнения";
        SaveButton.Content = "Создать этап";
        StatusRow.Visibility = Visibility.Collapsed;

        if (_fixedTaskId.HasValue)
        {
            ProjectRow.Visibility = Visibility.Collapsed;
            TaskRow.Visibility = Visibility.Collapsed;
            _ = LoadFixedTaskAsync(_fixedTaskId.Value);
        }
        else if (_fixedProjectId.HasValue)
        {
            ProjectRow.Visibility = Visibility.Collapsed;
            TaskRow.Visibility = Visibility.Visible;
            _ = LoadTasksForProjectAsync(_fixedProjectId.Value);
        }
        else
        {
            ProjectRow.Visibility = Visibility.Visible;
            TaskRow.Visibility = Visibility.Visible;
            _ = LoadProjectsAsync();
        }
    }

    public void SetEditMode(LocalTaskStage stage, LocalTask task,
        Func<System.Threading.Tasks.Task>? onSaved = null, Action? onAfterSave = null)
    {
        _editStage = stage;
        _task = task;
        _onSaved = onSaved;
        _onAfterSave = onAfterSave;
        TitleLabel.Text = "Редактировать этап";
        SubtitleLabel.Text = "Измените данные этапа и исполнителей";
        SaveButton.Content = "Сохранить изменения";
        StatusRow.Visibility = Visibility.Collapsed;

        NameBox.Text = stage.Name;
        DescriptionBox.Text = stage.Description ?? "";
        if (stage.DueDate.HasValue)
            DueDatePicker.SelectedDate = stage.DueDate.Value.ToDateTime(TimeOnly.MinValue);

        ProjectRow.Visibility = Visibility.Collapsed;
        TaskRow.Visibility = Visibility.Collapsed;
        ProjectReadOnlyRow.Visibility = Visibility.Visible;
        TaskReadOnlyRow.Visibility = Visibility.Visible;
        ProjectReadOnlyText.Text = task.ProjectName ?? "—";
        TaskReadOnlyText.Text = task.Name;

        _ = LoadAssigneesAsync(task.Id, stage.Id);
    }

    private async System.Threading.Tasks.Task LoadFixedTaskAsync(Guid taskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(taskId);
        if (task is null) return;
        _task = task;
        ProjectReadOnlyRow.Visibility = Visibility.Visible;
        TaskReadOnlyRow.Visibility = Visibility.Visible;
        ProjectReadOnlyText.Text = task.ProjectName ?? "—";
        TaskReadOnlyText.Text = task.Name;
        await LoadAssigneesAsync(taskId);
    }

    private async System.Threading.Tasks.Task LoadProjectsAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var projects = await db.Projects
            .Where(p => !p.IsArchived && !p.IsClosed && !p.IsMarkedForDeletion)
            .OrderBy(p => p.Name)
            .ToListAsync();
        ProjectCombo.ItemsSource = projects;
        if (projects.Count > 0)
        {
            ProjectCombo.SelectedIndex = 0;
            await LoadTasksForProjectAsync(projects[0].Id);
        }
    }

    private async System.Threading.Tasks.Task LoadTasksForProjectAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var auth = App.Services.GetRequiredService<IAuthService>();
        var currentUserRole = auth.UserRole ?? "";
        var currentUserId = auth.UserId;

        var tasksQuery = db.Tasks
            .Where(t => t.ProjectId == projectId && !t.IsArchived && !t.IsMarkedForDeletion);

        bool isForeman = string.Equals(currentUserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
        bool isWorker = string.Equals(currentUserRole, "Worker", StringComparison.OrdinalIgnoreCase);

        if (isForeman && currentUserId.HasValue)
        {
            var foremanTaskIds = await db.Tasks
                .Where(t => t.AssignedUserId == currentUserId.Value)
                .Select(t => t.Id)
                .ToListAsync();
            var foremanTaskIdsFromAssignees = await db.TaskAssignees
                .Where(ta => ta.UserId == currentUserId.Value)
                .Select(ta => ta.TaskId)
                .ToListAsync();
            var allForemanTaskIds = foremanTaskIds.Concat(foremanTaskIdsFromAssignees).Distinct().ToList();
            tasksQuery = tasksQuery.Where(t => allForemanTaskIds.Contains(t.Id));
        }
        else if (isWorker && currentUserId.HasValue)
        {
            var workerTaskIds = await db.Tasks
                .Where(t => t.AssignedUserId == currentUserId.Value)
                .Select(t => t.Id)
                .ToListAsync();
            var workerTaskIdsFromAssignees = await db.TaskAssignees
                .Where(ta => ta.UserId == currentUserId.Value)
                .Select(ta => ta.TaskId)
                .ToListAsync();
            var allWorkerTaskIds = workerTaskIds.Concat(workerTaskIdsFromAssignees).Distinct().ToList();
            tasksQuery = tasksQuery.Where(t => allWorkerTaskIds.Contains(t.Id));
        }

        var tasks = await tasksQuery.OrderBy(t => t.Name).ToListAsync();
        TaskCombo.ItemsSource = tasks;
        if (tasks.Count > 0)
        {
            TaskCombo.SelectedIndex = 0;
            if (TaskCombo.SelectedValue is Guid taskId)
                await LoadAssigneesAsync(taskId);
        }
        else
        {
            ClearAssignees();
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
            await LoadAssigneesAsync(taskId);
    }

    private async System.Threading.Tasks.Task LoadAssigneesAsync(Guid taskId, Guid? stageId = null)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var blockedUserIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
        var taskAssignees = await db.TaskAssignees
            .Where(ta => ta.TaskId == taskId && !blockedUserIds.Contains(ta.UserId))
            .OrderBy(ta => ta.UserName)
            .ToListAsync();

        var taskEntity = await db.Tasks.FindAsync(taskId);
        if (taskAssignees.Count == 0 && taskEntity?.AssignedUserId is { } legacyId
                                       && !blockedUserIds.Contains(legacyId))
        {
            taskAssignees.Add(new LocalTaskAssignee
            {
                TaskId = taskId,
                UserId = legacyId,
                UserName = taskEntity.AssignedUserName ?? "—"
            });
        }

        var auth = App.Services.GetRequiredService<IAuthService>();
        if (string.Equals(auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase)
            && !stageId.HasValue && auth.UserId.HasValue)
        {
            _selectedAssigneeIds.Clear();
            _selectedAssigneeIds.Add(auth.UserId.Value);
            if (taskAssignees.All(ta => ta.UserId != auth.UserId.Value))
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

        var userIds = taskAssignees.Select(ta => ta.UserId).Distinct().ToList();
        var userRows = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarPath, u.AvatarData, u.RoleName, u.SubRole, u.AdditionalSubRoles })
            .ToDictionaryAsync(u => u.Id);
        foreach (var ta in taskAssignees)
        {
            if (userRows.TryGetValue(ta.UserId, out var ur))
            {
                ta.AvatarPath = ur.AvatarPath;
                ta.AvatarData = ur.AvatarData;
            }
        }

        if (stageId.HasValue)
        {
            var stageAssignees = await db.StageAssignees
                .Where(sa => sa.StageId == stageId.Value && !blockedUserIds.Contains(sa.UserId))
                .ToListAsync();
            foreach (var sa in stageAssignees)
                _selectedAssigneeIds.Add(sa.UserId);
        }

        var allItems = taskAssignees
            .Where(ta =>
            {
                userRows.TryGetValue(ta.UserId, out var ur);
                var role = string.IsNullOrWhiteSpace(ur?.RoleName) ? "Worker" : ur.RoleName;
                return role is "Worker" or "Работник";
            })
            .Select(ta =>
            {
                userRows.TryGetValue(ta.UserId, out var ur);
                var role = string.IsNullOrWhiteSpace(ur?.RoleName) ? "Worker" : ur.RoleName;
                return new AssigneePickerItem(
                    ta.UserId,
                    ta.UserName,
                    role,
                    _selectedAssigneeIds,
                    ta.AvatarPath,
                    ta.AvatarData,
                    ur?.SubRole,
                    ur?.AdditionalSubRoles);
            })
            .ToList();

        _foremanItems = [];
        _workerItems = allItems;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_foremanItems.Count == 0 && _workerItems.Count == 0)
            {
                NoTaskHint.Visibility = Visibility.Visible;
                NoTaskHintText.Text = "В задаче нет назначенных исполнителей";
                ForemanPickerBorder.Visibility = Visibility.Collapsed;
                ForemanSectionTitle.Visibility = Visibility.Collapsed;
                WorkerPickerBorder.Visibility = Visibility.Collapsed;
                WorkerSectionTitle.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoTaskHint.Visibility = Visibility.Collapsed;
                ForemanPickerBorder.Visibility = _foremanItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ForemanSectionTitle.Visibility = _foremanItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                WorkerPickerBorder.Visibility = _workerItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                WorkerSectionTitle.Visibility = _workerItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshAssigneeItems();
        });
    }

    private void ClearAssignees()
    {
        _foremanItems = [];
        _workerItems = [];
        _selectedAssigneeIds.Clear();
        RefreshAssigneeItems();
        NoTaskHint.Visibility = Visibility.Visible;
        NoTaskHintText.Text = "Выберите задачу, чтобы увидеть доступных исполнителей";
        ForemanPickerBorder.Visibility = Visibility.Collapsed;
        ForemanSectionTitle.Visibility = Visibility.Collapsed;
        WorkerPickerBorder.Visibility = Visibility.Collapsed;
        WorkerSectionTitle.Visibility = Visibility.Collapsed;
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
        NoForemenHint.Visibility = _foremanItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoWorkersHint.Visibility = _workerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ForemanItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;
        var auth = App.Services.GetRequiredService<IAuthService>();
        if (item.UserId == auth.UserId && _editStage is null)
            return;
        if (_selectedAssigneeIds.Contains(item.UserId))
        {
            if (_editStage is null && _selectedAssigneeIds.Count <= 1)
            {
                ShowError("Назначьте хотя бы одного исполнителя");
                return;
            }
            _selectedAssigneeIds.Remove(item.UserId);
        }
        else
        {
            _selectedAssigneeIds.Add(item.UserId);
        }
        RefreshAssigneeItems();
    }

    private void WorkerItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;
        var auth = App.Services.GetRequiredService<IAuthService>();
        if (item.UserId == auth.UserId && _editStage is null)
            return;
        if (_selectedAssigneeIds.Contains(item.UserId))
        {
            if (_editStage is null && _selectedAssigneeIds.Count <= 1)
            {
                ShowError("Назначьте хотя бы одного исполнителя");
                return;
            }
            _selectedAssigneeIds.Remove(item.UserId);
        }
        else
        {
            _selectedAssigneeIds.Add(item.UserId);
        }
        RefreshAssigneeItems();
    }

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
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_onAfterSave is not null)
            _onAfterSave();
        else
            MainWindow.Instance?.HideOverlayLayer();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _errorHideTimer.Stop();
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название этапа"); return; }

        Guid taskId;
        LocalTask? task = _task;

        if (task is not null)
        {
            taskId = task.Id;
        }
        else if (_fixedTaskId.HasValue)
        {
            taskId = _fixedTaskId.Value;
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            task = await db.Tasks.FindAsync(taskId);
        }
        else if (TaskCombo.SelectedValue is Guid tid)
        {
            taskId = tid;
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            task = await db.Tasks.FindAsync(taskId);
        }
        else
        { ShowError("Выберите задачу"); return; }

        if (task is null)
        { ShowError("Задача не найдена"); return; }

        var auth = App.Services.GetRequiredService<IAuthService>();
        if (string.Equals(auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase) && _editStage is null)
        { ShowError("Работники не могут создавать этапы"); return; }

        if (!string.Equals(auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase) && _selectedAssigneeIds.Count == 0)
        { ShowError("Назначьте хотя бы одного работника на этап"); return; }

        if (string.Equals(auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase) && _editStage is null && auth.UserId.HasValue)
        {
            _selectedAssigneeIds.Clear();
            _selectedAssigneeIds.Add(auth.UserId.Value);
        }

        if (DueDatePicker.SelectedDate is null)
        { ShowError("Укажите срок выполнения этапа"); return; }

        DateOnly? dueDate = DueDatePicker.SelectedDate is { } sd ? DateOnly.FromDateTime(sd) : null;
        if (!DueDatePolicy.IsAllowedForUpdate(dueDate, _editStage?.DueDate))
        { ShowError(DueDatePolicy.PastNotAllowedMessage); return; }

        Guid? primaryAssigneeId = _selectedAssigneeIds.Count > 0 ? _selectedAssigneeIds.FirstOrDefault() : null;

        var taskVm = App.Services.GetRequiredService<TaskDetailViewModel>();
        taskVm.SetTask(task);

        SaveButton.IsEnabled = false;
        try
        {
            Guid stageId;
            if (_editStage is null)
            {
                var localId = Guid.NewGuid();
                var req = new CreateStageRequest(
                    taskId,
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId,
                    dueDate);
                await taskVm.SaveNewStageAsync(req, localId);
                stageId = localId;
            }
            else
            {
                var status = _editStage.Status;
                var req = new UpdateStageRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId,
                    status,
                    dueDate,
                    _editStage.IsMarkedForDeletion,
                    _editStage.IsArchived,
                    WorkTypeTemplateId: null,
                    WorkQuantity: 0,
                    WorkPricePerUnit: 0,
                    WorkTypeItems: null);
                await taskVm.SaveUpdatedStageAsync(_editStage.Id, req);
                stageId = _editStage.Id;
            }

            var assigneeRows = _selectedAssigneeIds
                .Select(uid =>
                {
                    var item = _workerItems.FirstOrDefault(i => i.UserId == uid)
                        ?? _foremanItems.FirstOrDefault(i => i.UserId == uid);
                    return (uid, item?.Name ?? "—");
                })
                .ToList();

            await taskVm.ReplaceStageAssigneesAsync(stageId, assigneeRows);

            if (_onSaved is not null)
                await _onSaved();
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

    private StageStatus GetStatus()
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "InProgress" => StageStatus.InProgress,
                "Completed" => StageStatus.Completed,
                _ => StageStatus.Planned
            };
        }
        return StageStatus.Planned;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        _errorHideTimer.Stop();
        _errorHideTimer.Start();
    }
}
