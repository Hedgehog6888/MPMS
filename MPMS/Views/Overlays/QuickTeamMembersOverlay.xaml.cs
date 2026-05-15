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

namespace MPMS.Views.Overlays;

public partial class QuickTeamMembersOverlay : UserControl
{
    private Guid? _projectId;
    private Guid? _stageId;
    private Func<System.Threading.Tasks.Task>? _onSaved;

    private List<AssigneePickerItem> _foremanItems = [];
    private List<AssigneePickerItem> _workerItems = [];
    private List<LocalUser> _foremanUsers = [];
    private List<LocalUser> _workerUsers = [];
    private readonly HashSet<Guid> _selectedForemanIds = [];
    private readonly HashSet<Guid> _selectedWorkerIds = [];
    private readonly System.Windows.Threading.DispatcherTimer _errorHideTimer;

    public QuickTeamMembersOverlay()
    {
        InitializeComponent();
        _errorHideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _errorHideTimer.Tick += (_, _) =>
        {
            _errorHideTimer.Stop();
            ErrorPanel.Visibility = Visibility.Collapsed;
        };
    }

    public void SetProject(Guid projectId, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _projectId = projectId;
        _stageId = null;
        _onSaved = onSaved;
        TitleText.Text = "Состав проекта";
        SubtitleText.Text = "Быстрое добавление/удаление прорабов и работников";
        _ = LoadDataAsync();
    }

    public void SetStage(Guid stageId, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _stageId = stageId;
        _projectId = null;
        _onSaved = onSaved;
        TitleText.Text = "Состав этапа";
        SubtitleText.Text = "Быстрое добавление/удаление прорабов и работников";
        _ = LoadDataAsync();
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        _foremanUsers = await db.Users
            .Where(u => !u.IsBlocked && (u.RoleName == "Foreman" || u.RoleName == "Прораб"))
            .OrderBy(u => u.Name)
            .ToListAsync();

        _workerUsers = await db.Users
            .Where(u => !u.IsBlocked && (u.RoleName == "Worker" || u.RoleName == "Работник"))
            .OrderBy(u => u.Name)
            .ToListAsync();

        _selectedForemanIds.Clear();
        _selectedWorkerIds.Clear();

        if (_projectId.HasValue)
        {
            var members = await db.ProjectMembers
                .Where(m => m.ProjectId == _projectId.Value)
                .ToListAsync();

            foreach (var fm in members.Where(m => m.UserRole is "Foreman" or "Прораб"))
            {
                if (_foremanUsers.Any(u => u.Id == fm.UserId))
                    _selectedForemanIds.Add(fm.UserId);
            }

            foreach (var wm in members.Where(m => m.UserRole is "Worker" or "Работник"))
            {
                if (_workerUsers.Any(u => u.Id == wm.UserId))
                    _selectedWorkerIds.Add(wm.UserId);
            }
        }
        else if (_stageId.HasValue)
        {
            var assignees = await db.StageAssignees
                .Where(a => a.StageId == _stageId.Value)
                .ToListAsync();

            foreach (var fa in assignees)
            {
                if (_foremanUsers.Any(u => u.Id == fa.UserId))
                    _selectedForemanIds.Add(fa.UserId);
                else if (_workerUsers.Any(u => u.Id == fa.UserId))
                    _selectedWorkerIds.Add(fa.UserId);
            }
        }

        _foremanItems = _foremanUsers
            .Select(u => new AssigneePickerItem(u.Id, u.Name, "Foreman", _selectedForemanIds, u.AvatarPath, u.AvatarData))
            .ToList();

        _workerItems = _workerUsers
            .Select(u => new AssigneePickerItem(
                u.Id, u.Name, "Worker", _selectedWorkerIds, u.AvatarPath, u.AvatarData, u.SubRole, u.AdditionalSubRoles))
            .ToList();

        ForemanPickerList.ItemsSource = _foremanItems;
        WorkerPickerList.ItemsSource = _workerItems;
        NoForemanHint.Visibility = _foremanItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoWorkersHint.Visibility = _workerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RefreshForemanItemsAndChips();
        RefreshWorkerItemsAndChips();
    }

    private void ForemanItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;

        if (_selectedForemanIds.Contains(item.UserId))
        {
            if (_projectId.HasValue && _selectedForemanIds.Count <= 1)
            {
                ShowError("В проекте должен остаться хотя бы один прораб.");
                return;
            }
            _selectedForemanIds.Remove(item.UserId);
        }
        else
        {
            _selectedForemanIds.Add(item.UserId);
            _errorHideTimer.Stop();
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        RefreshForemanItemsAndChips();
    }

    private void WorkerItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;

        if (_selectedWorkerIds.Contains(item.UserId))
        {
            if (_projectId.HasValue && _selectedWorkerIds.Count <= 1)
            {
                ShowError("В проекте должен остаться хотя бы один работник.");
                return;
            }
            _selectedWorkerIds.Remove(item.UserId);
        }
        else
        {
            _selectedWorkerIds.Add(item.UserId);
            _errorHideTimer.Stop();
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        RefreshWorkerItemsAndChips();
    }

    private void RefreshForemanItemsAndChips()
    {
        foreach (var item in _foremanItems)
            item.RefreshSelected(_selectedForemanIds);
        ForemanPickerList.ItemsSource = null;
        ForemanPickerList.ItemsSource = _foremanItems;
    }

    private void RefreshWorkerItemsAndChips()
    {
        foreach (var item in _workerItems)
            item.RefreshSelected(_selectedWorkerIds);
        WorkerPickerList.ItemsSource = null;
        WorkerPickerList.ItemsSource = _workerItems;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _errorHideTimer.Stop();
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (_projectId.HasValue)
        {
            if (_selectedForemanIds.Count == 0)
            {
                ShowError("Добавьте хотя бы одного прораба.");
                return;
            }
            if (_selectedWorkerIds.Count == 0)
            {
                ShowError("Добавьте хотя бы одного работника.");
                return;
            }
        }

        SaveButton.IsEnabled = false;
        try
        {
            if (_projectId.HasValue)
                await SaveProjectMembersAsync();
            else if (_stageId.HasValue)
                await SaveStageAssigneesAsync();

            if (_onSaved is not null)
                await _onSaved();
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

    private async System.Threading.Tasks.Task SaveProjectMembersAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (_projectId is null) return;

        var newMemberIds = new HashSet<Guid>();
        foreach (var foremanId in _selectedForemanIds) newMemberIds.Add(foremanId);
        foreach (var workerId in _selectedWorkerIds) newMemberIds.Add(workerId);

        var existing = await db.ProjectMembers
            .Where(m => m.ProjectId == _projectId.Value)
            .ToListAsync();
        var removedIds = existing
            .Where(m => !newMemberIds.Contains(m.UserId))
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        db.ProjectMembers.RemoveRange(existing);

        foreach (var foremanId in _selectedForemanIds)
        {
            var foreman = _foremanUsers.FirstOrDefault(u => u.Id == foremanId);
            if (foreman is null) continue;
            db.ProjectMembers.Add(new LocalProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = _projectId.Value,
                UserId = foremanId,
                UserName = foreman.Name,
                UserRole = "Foreman"
            });
        }

        foreach (var workerId in _selectedWorkerIds)
        {
            var worker = _workerUsers.FirstOrDefault(u => u.Id == workerId);
            if (worker is null) continue;
            db.ProjectMembers.Add(new LocalProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = _projectId.Value,
                UserId = workerId,
                UserName = worker.Name,
                UserRole = "Worker"
            });
        }

        var project = await db.Projects.FindAsync(_projectId.Value);
        var totalMembers = _selectedForemanIds.Count + _selectedWorkerIds.Count;
        if (totalMembers > 0 && project != null)
        {
            var memberNames = new List<string>();
            foreach (var foremanId in _selectedForemanIds)
            {
                var foreman = _foremanUsers.FirstOrDefault(u => u.Id == foremanId);
                if (foreman != null) memberNames.Add(foreman.Name);
            }
            foreach (var workerId in _selectedWorkerIds)
            {
                var worker = _workerUsers.FirstOrDefault(u => u.Id == workerId);
                if (worker != null) memberNames.Add(worker.Name);
            }
            var namesText = string.Join(", ", memberNames);
            await LogActivityAsync(db, $"В проект «{project.Name}» добавлены участники: {namesText}", "Project", _projectId.Value, ActivityActionKind.MemberAdded);
        }

        if (removedIds.Count > 0)
        {
            var taskIds = await db.Tasks.Where(t => t.ProjectId == _projectId.Value).Select(t => t.Id).ToListAsync();
            var toRemoveTaskAssignees = await db.TaskAssignees
                .Where(ta => taskIds.Contains(ta.TaskId) && removedIds.Contains(ta.UserId))
                .ToListAsync();
            db.TaskAssignees.RemoveRange(toRemoveTaskAssignees);

            var stageIds = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).Select(s => s.Id).ToListAsync();
            var toRemoveStageAssignees = await db.StageAssignees
                .Where(sa => stageIds.Contains(sa.StageId) && removedIds.Contains(sa.UserId))
                .ToListAsync();
            db.StageAssignees.RemoveRange(toRemoveStageAssignees);
        }

        await db.SaveChangesAsync();
    }

    private async System.Threading.Tasks.Task SaveStageAssigneesAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (_stageId is null) return;

        var newAssigneeIds = new HashSet<Guid>();
        foreach (var foremanId in _selectedForemanIds) newAssigneeIds.Add(foremanId);
        foreach (var workerId in _selectedWorkerIds) newAssigneeIds.Add(workerId);

        var existing = await db.StageAssignees
            .Where(a => a.StageId == _stageId.Value)
            .ToListAsync();

        db.StageAssignees.RemoveRange(existing);

        foreach (var foremanId in _selectedForemanIds)
        {
            var foreman = _foremanUsers.FirstOrDefault(u => u.Id == foremanId);
            if (foreman is null) continue;
            db.StageAssignees.Add(new LocalStageAssignee
            {
                Id = Guid.NewGuid(),
                StageId = _stageId.Value,
                UserId = foremanId,
                UserName = foreman.Name
            });
        }

        foreach (var workerId in _selectedWorkerIds)
        {
            var worker = _workerUsers.FirstOrDefault(u => u.Id == workerId);
            if (worker is null) continue;
            db.StageAssignees.Add(new LocalStageAssignee
            {
                Id = Guid.NewGuid(),
                StageId = _stageId.Value,
                UserId = workerId,
                UserName = worker.Name
            });
        }

        var stage = await db.TaskStages.FindAsync(_stageId.Value);
        var totalAssignees = _selectedForemanIds.Count + _selectedWorkerIds.Count;
        if (totalAssignees > 0 && stage != null)
        {
            var assigneeNames = new List<string>();
            foreach (var foremanId in _selectedForemanIds)
            {
                var foreman = _foremanUsers.FirstOrDefault(u => u.Id == foremanId);
                if (foreman != null) assigneeNames.Add(foreman.Name);
            }
            foreach (var workerId in _selectedWorkerIds)
            {
                var worker = _workerUsers.FirstOrDefault(u => u.Id == workerId);
                if (worker != null) assigneeNames.Add(worker.Name);
            }
            var namesText = string.Join(", ", assigneeNames);
            await LogActivityAsync(db, $"В этап «{stage.Name}» добавлены участники: {namesText}", "Stage", _stageId.Value, ActivityActionKind.MemberAdded);
        }

        await db.SaveChangesAsync();
    }

    private static async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId, string actionType)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var userName = auth.UserName ?? "Система";
        var userId = auth.UserId;
        var userRole = auth.UserRole;

        db.ActivityLogs.Add(new LocalActivityLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActorRole = userRole,
            ActionType = actionType,
            ActionText = actionText,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
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
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        _errorHideTimer.Stop();
        _errorHideTimer.Start();
    }
}
