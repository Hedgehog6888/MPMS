using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class TaskDetailOverlay : UserControl
{
    /// <summary>Как показывать drawer при открытии и после вложенных форм (этап, редактирование задачи).</summary>
    public enum TaskDetailDrawerMode
    {
        /// <summary>Только карточка задачи — пользователь уже на странице проекта.</summary>
        TaskOnly,
        /// <summary>Сводка проекта слева (глобальный поиск, страница «Задачи»).</summary>
        WithProjectSummary,
    }

    private TaskDetailViewModel? _vm;
    private Action? _onClosed;
    private TaskDetailDrawerMode _drawerMode = TaskDetailDrawerMode.WithProjectSummary;

    public TaskDetailOverlay()
    {
        InitializeComponent();
    }

    public void SetTask(LocalTask task, Action? onClosed = null,
        TaskDetailDrawerMode drawerMode = TaskDetailDrawerMode.WithProjectSummary)
    {
        _onClosed = onClosed;
        _drawerMode = drawerMode;
        _vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        _vm.SetTask(task);
        DataContext = _vm;
        _ = LoadDataAsync();
        ApplyRoleRestrictions();
    }

    private void ApplyRoleRestrictions()
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        bool isWorker   = string.Equals(role, "Worker",   StringComparison.OrdinalIgnoreCase);
        bool isForeman  = string.Equals(role, "Foreman",  StringComparison.OrdinalIgnoreCase);
        bool isManager  = role is "Manager" or "ProjectManager" or "Project Manager";
        bool isAdmin    = role is "Admin" or "Administrator";

        if (isWorker)
        {
            EditTaskBtn.Visibility    = Visibility.Collapsed;
            AddStageBtn.Visibility    = Visibility.Collapsed;
            MarkDeletionBtn.Visibility = Visibility.Collapsed;
        }
        else if (!isForeman && !isManager && !isAdmin)
        {
            MarkDeletionBtn.Visibility = Visibility.Collapsed;
        }
        else if (_vm?.Task?.CanToggleTaskDeletionMark == false)
        {
            MarkDeletionBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            MarkDeletionBtn.Visibility = Visibility.Visible;
        }

        if (isWorker)
        {
            /* Edit already collapsed */
        }
        else if (_vm?.Task?.EffectiveTaskMarkedForDeletion == true)
        {
            EditTaskBtn.Visibility = Visibility.Collapsed;
        }
        else if (isForeman || isManager || isAdmin)
        {
            EditTaskBtn.Visibility = Visibility.Visible;
        }
        else
        {
            EditTaskBtn.Visibility = Visibility.Collapsed;
        }
        ApplyDeletionFooter();
        // Статус задачи вычисляется автоматически из этапов — ручное изменение скрыто
        ChangeStatusBtn.Visibility = Visibility.Collapsed;

    }

    private void ApplyDeletionFooter()
    {
        var t = _vm?.Task;
        if (t is null) return;
        MarkDeletionBtnText.Text = t.IsMarkedForDeletion ? "Снять пометку" : "Пометить к удалению";
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        if (_vm is null) return;
        await _vm.LoadAsync();
        UpdateStagesTabLabel();
        UpdateEmptyStates();
        await LoadAssigneesAsync();
        ApplyRoleRestrictions();
    }

    private async System.Threading.Tasks.Task LoadAssigneesAsync()
    {
        if (_vm?.Task is null) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var assignees = await db.TaskAssignees
            .Where(a => a.TaskId == _vm.Task.Id)
            .OrderBy(a => a.UserName)
            .ToListAsync();

        // If no multi-assignees, fall back to legacy single assignee
        if (assignees.Count == 0 && _vm.Task.AssignedUserId.HasValue)
        {
            assignees.Add(new LocalTaskAssignee
            {
                TaskId = _vm.Task.Id,
                UserId = _vm.Task.AssignedUserId.Value,
                UserName = _vm.Task.AssignedUserName ?? "—"
            });
        }

        var roleMap    = new Dictionary<Guid, string?>();
        var subRoleMap = new Dictionary<Guid, string?>();
        var addSpecMap = new Dictionary<Guid, string?>();
        var userIds = assignees.Select(a => a.UserId).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var users = await db.Users.Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath, u.RoleName, u.SubRole, u.AdditionalSubRoles })
                .ToListAsync();
            var avDict = users.ToDictionary(u => u.Id);
            roleMap    = users.ToDictionary(u => u.Id, u => (string?)u.RoleName);
            subRoleMap = users.ToDictionary(u => u.Id, u => (string?)u.SubRole);
            addSpecMap = users.ToDictionary(u => u.Id, u => u.AdditionalSubRoles);
            foreach (var a in assignees)
            {
                if (avDict.TryGetValue(a.UserId, out var av))
                {
                    a.AvatarData = av.AvatarData;
                    a.AvatarPath = av.AvatarPath;
                }
            }
        }
        var displayItems = assignees
            .Select(a =>
            {
                var role    = roleMap.TryGetValue(a.UserId, out var userRole) ? userRole : null;
                var subRole = subRoleMap.TryGetValue(a.UserId, out var sr) ? sr : null;
                var addSpec = addSpecMap.TryGetValue(a.UserId, out var aj) ? aj : null;
                return new AssigneeDisplayItem(a.UserId, a.UserName, role, a.AvatarData, a.AvatarPath, subRole, addSpec);
            })
            .ToList();
        var foremen = displayItems.Where(a => a.RoleDisplay == "Прораб").ToList();
        var workers = displayItems.Where(a => a.RoleDisplay != "Прораб").ToList();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ForemenDisplay.ItemsSource = foremen;
            WorkersDisplay.ItemsSource = workers;
            ForemenSection.Visibility = foremen.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            WorkersSection.Visibility = workers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NoAssigneesText.Visibility = displayItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void UpdateStagesTabLabel()
    {
        if (_vm is null) return;
        StagesTab.Content = _vm.Stages.Count > 0
            ? $"Этапы ({_vm.Stages.Count})"
            : "Этапы";
    }

    private void UpdateEmptyStates()
    {
        if (_vm is null) return;
        _vm.HasNoStages = _vm.Stages.Count == 0;
        _vm.HasNoMaterials = _vm.AllMaterials.Count == 0;
        _vm.HasNoFiles = _vm.Files.Count == 0;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _onClosed?.Invoke();
        MainWindow.Instance?.HideDrawer();
    }

    private void InnerTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        string tag = rb.Tag as string ?? "";

        StagesPanel.Visibility   = tag == "Stages"    ? Visibility.Visible : Visibility.Collapsed;
        MaterialsPanel.Visibility = tag == "Materials" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility     = tag == "Files"     ? Visibility.Visible : Visibility.Collapsed;
        MessagesPanel.Visibility = tag == "Messages"   ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void MarkTaskForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;
        await _vm.MarkTaskForDeletionCommand.ExecuteAsync(null);
        ApplyRoleRestrictions();
        _onClosed?.Invoke();
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetEditMode(
            _vm.Task,
            onSaved: async () =>
            {
                await _vm.LoadAsync();
                UpdateStagesTabLabel();
                UpdateEmptyStates();
                _onClosed?.Invoke();
            },
            onAfterSave: () => _ = ReopenTaskDetailDualAsync());

        // При редактировании скрываем текущую пару оверлеев
        // и показываем только окно редактирования.
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async System.Threading.Tasks.Task ReopenTaskDetailDualAsync()
    {
        if (_vm?.Task is null) return;

        if (_drawerMode == TaskDetailDrawerMode.TaskOnly)
        {
            var detail = new TaskDetailOverlay();
            detail.SetTask(_vm.Task, _onClosed, TaskDetailDrawerMode.TaskOnly);
            MainWindow.Instance?.ShowDrawer(detail, MainWindow.TaskOrStageDetailDrawerWidth);
            return;
        }

        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        var project = await tasksVm.GetProjectForTaskAsync(_vm.Task.ProjectId);

        UIElement? leftPanel = null;
        ProjectSummaryPanel? projectPanel = null;
        if (project is not null)
        {
            projectPanel = new ProjectSummaryPanel();
            projectPanel.SetProject(project);
            leftPanel = projectPanel;
        }

        var projectId = _vm.Task.ProjectId;
        var detailDual = new TaskDetailOverlay();
        detailDual.SetTask(_vm.Task, () =>
        {
            _onClosed?.Invoke();
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var updatedProject = await tasksVm.GetProjectForTaskAsync(projectId);
                if (updatedProject != null && projectPanel != null)
                    projectPanel.SetProject(updatedProject);
            });
        }, TaskDetailDrawerMode.WithProjectSummary);
        MainWindow.Instance?.ShowDrawer(leftPanel, detailDual, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
    }

    private void ChangeStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;

        var menuStyle = Application.Current.FindResource("StatusMenu") as Style;
        var itemStyle = Application.Current.FindResource("StatusMenuItem") as Style;
        var menu = new ContextMenu { Style = menuStyle };

        void AddItem(string label, TaskStatus status)
        {
            var item = new MenuItem { Header = label, Style = itemStyle };
            item.Click += async (s, _) =>
            {
                await _vm.ChangeTaskStatusAsync(status);
                // Notify project page to refresh and close drawer
                _onClosed?.Invoke();
                MainWindow.Instance?.HideDrawer();
            };
            menu.Items.Add(item);
        }

        AddItem("Запланирована", TaskStatus.Planned);
        AddItem("Выполняется", TaskStatus.InProgress);
        AddItem("Приостановлена", TaskStatus.Paused);
        AddItem("Завершена", TaskStatus.Completed);

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void AddStage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;
        var overlay = new CreateStageOverlay();
        overlay.SetTask(
            _vm.Task,
            onSaved: async () =>
            {
                await _vm.LoadAsync();
                UpdateStagesTabLabel();
                UpdateEmptyStates();
                _onClosed?.Invoke(); // Refresh project page
            },
            onAfterSave: () => _ = ReopenTaskDetailDualAsync());

        MainWindow.Instance?.ShowDrawer(overlay, 500);
    }

    private void UploadFiles_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Загрузка файлов будет доступна в следующей версии.", "Информация",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void StartStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null || stage.IsMarkedForDeletion) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.InProgress));
        _onClosed?.Invoke(); // Sync project page
    }

    private async void CompleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null || stage.IsMarkedForDeletion) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.Completed));
        _onClosed?.Invoke(); // Sync project page
    }

    private async void RevertStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null || stage.IsMarkedForDeletion) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.Planned));
        _onClosed?.Invoke(); // Sync project page
    }

    private async void ReopenStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null || stage.IsMarkedForDeletion) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.InProgress));
        _onClosed?.Invoke(); // Sync project page
    }

    private async void SendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || string.IsNullOrWhiteSpace(MessageInput.Text)) return;
        await _vm.SendMessageAsync(MessageInput.Text);
        MessageInput.Clear();
    }

    private async void MessageInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && _vm is not null && !string.IsNullOrWhiteSpace(MessageInput.Text))
        {
            await _vm.SendMessageAsync(MessageInput.Text);
            MessageInput.Clear();
            e.Handled = true;
        }
    }

    private async void MarkStageForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        await _vm.MarkStageForDeletionCommand.ExecuteAsync(stage);
        _onClosed?.Invoke(); // Синхронизация страницы проекта
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        var owner = Window.GetWindow(this);
        if (MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(owner, "Этап", stage.Name))
        {
            await _vm.DeleteStageCommand.ExecuteAsync(stage);
            _onClosed?.Invoke(); // Синхронизация страницы проекта
        }
    }

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm?.Task is null) return;

        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(
            stage,
            _vm.Task,
            onSaved: async () =>
            {
                await _vm.LoadAsync();
                UpdateStagesTabLabel();
                UpdateEmptyStates();
                _onClosed?.Invoke(); // Sync project page
            },
            onAfterSave: () => _ = ReopenTaskDetailDualAsync());

        // Только форма этапа; после закрытия — снова деталь задачи в том же режиме drawer.
        MainWindow.Instance?.ShowDrawer(overlay, 500);
    }
}

/// <summary>Display model for a task assignee in the detail overlay.</summary>
public sealed class AssigneeDisplayItem
{
    public Guid UserId { get; }
    public string UserName { get; }
    public string RoleDisplay { get; }
    public string SubRoleLabel { get; }
    public Visibility SubRoleLabelVis =>
        string.IsNullOrWhiteSpace(SubRoleLabel) ? Visibility.Collapsed : Visibility.Visible;
    public SolidColorBrush SubRoleLabelBrush { get; }
    public string Initials { get; }
    public byte[]? AvatarData { get; }
    public string? AvatarPath { get; }

    public AssigneeDisplayItem(Guid userId, string userName, string? roleName, byte[]? avatarData = null, string? avatarPath = null, string? subRole = null, string? additionalSubRolesJson = null)
    {
        UserId = userId;
        UserName = userName;
        var label = ProjectDetailViewModel.RoleToRussian(roleName);
        RoleDisplay = label == "—" ? "Работник" : label;
        var isWorker = roleName is "Worker" or "Работник";
        SubRoleLabel = isWorker
            ? WorkerSpecialtiesJson.FormatWorkerLineCompact(subRole, additionalSubRolesJson)
            : "";
        if (isWorker)
        {
            var fg = WorkerSpecialtiesJson.BadgeForegroundRgbForSpecName(
                WorkerSpecialtiesJson.PrimaryDisplaySpecForColor(subRole, additionalSubRolesJson));
            SubRoleLabelBrush = new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B));
        }
        else
            SubRoleLabelBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));
        AvatarData = avatarData;
        AvatarPath = avatarPath;
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : userName.Length > 0 ? userName[0].ToString().ToUpper() : "?";
    }
}
