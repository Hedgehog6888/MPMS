using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class StageDetailOverlay : UserControl
{
    private LocalTaskStage? _stage;
    private LocalTask? _task;
    private Action? _onClosed;

    public StageDetailOverlay()
    {
        InitializeComponent();
    }

    public void SetStage(StageItem item, LocalTask task, Action? onClosed = null)
    {
        _stage = item.Stage;
        _task = task;
        _onClosed = onClosed;

        StageNameText.Text = item.Stage.Name;
        TaskNameText.Text = item.TaskName ?? task.Name;

        DescriptionText.Text = string.IsNullOrWhiteSpace(item.Stage.Description) ? "Описание не указано" : item.Stage.Description;

        item.Stage.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
        item.Stage.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;
        ApplyStatus(item.Stage.Status);
        ApplyDeletionUi();

        _ = EnsureStageDeletionFlagsFromDbAsync();
        _ = LoadAssigneesAsync();
        _ = LoadMaterialsAsync();
    }

    private async System.Threading.Tasks.Task EnsureStageDeletionFlagsFromDbAsync()
    {
        if (_stage is null || _task is null) return;
        var stageRef = _stage;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var proj = await db.Projects.FindAsync(_task.ProjectId);
        var taskEnt = await db.Tasks.FindAsync(_task.Id);

        await Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_stage, stageRef)) return;
            if (taskEnt != null)
            {
                _task.IsMarkedForDeletion = taskEnt.IsMarkedForDeletion;
                _task.ProjectIsMarkedForDeletion = proj?.IsMarkedForDeletion ?? false;
            }
            _stage.TaskIsMarkedForDeletion = _task.IsMarkedForDeletion;
            _stage.ProjectIsMarkedForDeletion = _task.ProjectIsMarkedForDeletion;
            ApplyStatus(_stage.Status);
            ApplyDeletionUi();
        });
    }

    private void ApplyStatus(StageStatus status)
    {
        var brush = StageStatusToBrushConverter.Instance.Convert(status, typeof(Brush), null!, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = brush ?? Brushes.Gray;
        StatusText.Text = StageStatusToStringConverter.Instance.Convert(status, typeof(string), null!, CultureInfo.InvariantCulture) as string ?? "—";

        var neutral = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
        var neutralBg = new SolidColorBrush(Colors.White);
        var neutralFg = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));

        BtnPlanned.BorderBrush = status == StageStatus.Planned
            ? new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x4D)) : neutral;
        BtnPlanned.Background = status == StageStatus.Planned
            ? new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)) : neutralBg;
        BtnPlanned.Foreground = status == StageStatus.Planned
            ? new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x4D)) : neutralFg;
        BtnPlanned.FontWeight = status == StageStatus.Planned ? FontWeights.SemiBold : FontWeights.Normal;

        BtnInProgress.BorderBrush = status == StageStatus.InProgress
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)) : neutral;
        BtnInProgress.Background = status == StageStatus.InProgress
            ? new SolidColorBrush(Color.FromRgb(0xEB, 0xF2, 0xFF)) : neutralBg;
        BtnInProgress.Foreground = status == StageStatus.InProgress
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2)) : neutralFg;
        BtnInProgress.FontWeight = status == StageStatus.InProgress ? FontWeights.SemiBold : FontWeights.Normal;

        BtnCompleted.BorderBrush = status == StageStatus.Completed
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutral;
        BtnCompleted.Background = status == StageStatus.Completed
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)) : neutralBg;
        BtnCompleted.Foreground = status == StageStatus.Completed
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutralFg;
        BtnCompleted.FontWeight = status == StageStatus.Completed ? FontWeights.SemiBold : FontWeights.Normal;

        if (_stage != null && _stage.EffectiveMarkedForDeletion)
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B));
            StatusText.Text = "Пометка удаления";
        }
    }

    private void ApplyDeletionUi()
    {
        if (_stage is null) return;
        bool eff = _stage.EffectiveMarkedForDeletion;
        DeletionWarningBorder.Visibility = eff ? Visibility.Visible : Visibility.Collapsed;
        var hint = _stage.StageInheritedDeletionHint ?? "";
        DeletionHintText.Text = hint;
        DeletionHintText.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        MarkDeletionBtn.Visibility = _stage.CanToggleStageDeletionMark ? Visibility.Visible : Visibility.Collapsed;
        MarkDeletionBtnText.Text = _stage.IsMarkedForDeletion ? "Снять пометку" : "Пометить к удалению";
        EditButton.Visibility = eff ? Visibility.Collapsed : Visibility.Visible;
        BtnPlanned.IsEnabled = !eff;
        BtnInProgress.IsEnabled = !eff;
        BtnCompleted.IsEnabled = !eff;
    }

    private async System.Threading.Tasks.Task LoadAssigneesAsync()
    {
        if (_stage is null) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var assignees = await db.StageAssignees
            .Where(a => a.StageId == _stage.Id)
            .OrderBy(a => a.UserName)
            .ToListAsync();

        // Fallback to legacy single assignee
        if (assignees.Count == 0 && _stage.AssignedUserId.HasValue)
        {
            assignees.Add(new LocalStageAssignee
            {
                StageId = _stage.Id,
                UserId = _stage.AssignedUserId.Value,
                UserName = _stage.AssignedUserName ?? "—"
            });
        }

        var userIds = assignees.Select(a => a.UserId).Distinct().ToList();
        var roleByUser = new Dictionary<Guid, string?>();
        if (userIds.Count > 0)
        {
            var userRows = await db.Users.Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath, u.RoleName })
                .ToListAsync();
            var avDict = userRows.ToDictionary(u => u.Id);
            roleByUser = userRows.ToDictionary(u => u.Id, u => (string?)u.RoleName);
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
                roleByUser.TryGetValue(a.UserId, out var role);
                return new AssigneeDisplayItem(a.UserId, a.UserName, role, a.AvatarData, a.AvatarPath);
            })
            .ToList();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (displayItems.Count == 0)
            {
                AssigneesList.Visibility = Visibility.Collapsed;
                NoAssigneesText.Visibility = Visibility.Visible;
            }
            else
            {
                AssigneesList.ItemsSource = displayItems;
                AssigneesList.Visibility = Visibility.Visible;
                NoAssigneesText.Visibility = Visibility.Collapsed;
            }
        });
    }

    private async System.Threading.Tasks.Task LoadMaterialsAsync()
    {
        if (_stage is null) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var mats = await db.StageMaterials
            .Where(sm => sm.StageId == _stage.Id)
            .ToListAsync();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (mats.Count == 0)
            {
                MaterialsList.Visibility = Visibility.Collapsed;
                NoMaterialsState.Visibility = Visibility.Visible;
            }
            else
            {
                MaterialsList.ItemsSource = mats;
                MaterialsList.Visibility = Visibility.Visible;
                NoMaterialsState.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _onClosed?.Invoke();
        MainWindow.Instance?.HideDrawer();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_stage is null || _task is null) return;
        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(
            _stage,
            _task,
            onSaved: () =>
            {
                _onClosed?.Invoke();
                return System.Threading.Tasks.Task.CompletedTask;
            },
            onAfterSave: () => _ = ReopenStageDetailAsync());
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void SetStatusPlanned_Click(object sender, RoutedEventArgs e)
        => await ChangeStatusAsync(StageStatus.Planned);

    private async void SetStatusInProgress_Click(object sender, RoutedEventArgs e)
        => await ChangeStatusAsync(StageStatus.InProgress);

    private async void SetStatusCompleted_Click(object sender, RoutedEventArgs e)
        => await ChangeStatusAsync(StageStatus.Completed);

    private async System.Threading.Tasks.Task ChangeStatusAsync(StageStatus newStatus)
    {
        if (_stage is null || _stage.EffectiveMarkedForDeletion) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(_stage.Id);
        if (entity is null) return;

        entity.Status = newStatus;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LastModifiedLocally = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Recalculate task progress and project status
        await RecalcTaskProgressAsync(db, entity.TaskId);

        _stage.Status = newStatus;
        ApplyStatus(newStatus);
        _onClosed?.Invoke(); // Refresh parent list
    }

    private static async System.Threading.Tasks.Task RecalcTaskProgressAsync(LocalDbContext db, Guid taskId)
    {
        var task = await db.Tasks.FindAsync(taskId);
        if (task is null) return;
        var proj = await db.Projects.FindAsync(task.ProjectId);
        task.ProjectIsMarkedForDeletion = proj?.IsMarkedForDeletion ?? false;

        var stages = await db.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
        foreach (var s in stages)
        {
            s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
            s.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;
        }
        ProgressCalculator.ApplyTaskMetrics(task, stages);

        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await RecalcProjectStatusAsync(db, task.ProjectId);
    }

    private static async System.Threading.Tasks.Task RecalcProjectStatusAsync(LocalDbContext db, Guid projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && !t.IsMarkedForDeletion).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = taskIds.Count == 0
            ? new List<LocalTaskStage>()
            : await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).ToListAsync();

        foreach (var task in tasks)
        {
            task.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
            var taskStages = stages.Where(s => s.TaskId == task.Id).ToList();
            foreach (var s in taskStages)
            {
                s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
                s.ProjectIsMarkedForDeletion = project.IsMarkedForDeletion;
            }
            ProgressCalculator.ApplyTaskMetrics(task, taskStages);
        }

        project.Status = StatusCalculator.GetProjectStatusFromTasks(tasks);
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async void MarkDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (_stage is null || _task is null || !_stage.CanToggleStageDeletionMark) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(_stage.Id);
        if (entity is null) return;
        var taskEntity = await db.Tasks.FindAsync(entity.TaskId);
        var proj = taskEntity is not null ? await db.Projects.FindAsync(taskEntity.ProjectId) : null;
        if (taskEntity?.IsMarkedForDeletion == true || proj?.IsMarkedForDeletion == true)
            return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.LastModifiedLocally = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _stage.IsMarkedForDeletion = entity.IsMarkedForDeletion;
        if (taskEntity != null)
        {
            _task!.IsMarkedForDeletion = taskEntity.IsMarkedForDeletion;
            _task.ProjectIsMarkedForDeletion = proj?.IsMarkedForDeletion ?? false;
        }
        _stage.TaskIsMarkedForDeletion = _task!.IsMarkedForDeletion;
        _stage.ProjectIsMarkedForDeletion = _task.ProjectIsMarkedForDeletion;
        ApplyStatus(_stage.Status);
        ApplyDeletionUi();
        _onClosed?.Invoke();
    }

    private async System.Threading.Tasks.Task ReopenStageDetailAsync()
    {
        if (_stage is null || _task is null) return;

        var taskPanel = new TaskSummaryPanel();
        taskPanel.SetTask(_task);

        var item = new MPMS.ViewModels.StageItem
        {
            Stage       = _stage,
            TaskId      = _task.Id,
            TaskName    = _task.Name,
            ProjectId   = _task.ProjectId,
            ProjectName = _task.ProjectName ?? "—"
        };

        var taskId = _task.Id;
        var stageOverlay = new StageDetailOverlay();
        stageOverlay.SetStage(item, _task, () =>
        {
            _onClosed?.Invoke();
            _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var updatedTask = await db.Tasks.FindAsync(taskId);
                if (updatedTask != null)
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => taskPanel.SetTask(updatedTask));
            });
        });

        MainWindow.Instance?.ShowDrawer(taskPanel, stageOverlay, 850);
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
