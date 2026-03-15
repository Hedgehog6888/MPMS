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

        ApplyStatus(item.Stage.Status);
        ApplyDeletionMark(item.Stage.IsMarkedForDeletion);

        _ = LoadAssigneesAsync();
        _ = LoadMaterialsAsync();
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
    }

    private void ApplyDeletionMark(bool isMarked)
    {
        DeletionWarningBorder.Visibility = isMarked ? Visibility.Visible : Visibility.Collapsed;
        MarkDeletionBtn.Content = isMarked ? "Снять пометку" : "Пометить к удалению";
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

        var displayItems = assignees.Select(a => new AssigneeDisplayItem(a.UserId, a.UserName)).ToList();

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
        if (_stage is null) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(_stage.Id);
        if (entity is null) return;

        entity.Status = newStatus;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
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

        var stages = await db.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
        task.TotalStages = stages.Count;
        task.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);

        if (stages.Count > 0)
        {
            if (stages.All(s => s.Status == StageStatus.Completed))
                task.Status = TaskStatus.Completed;
            else if (stages.Any(s => s.Status == StageStatus.InProgress))
                task.Status = TaskStatus.InProgress;
            else
                task.Status = TaskStatus.Planned;
        }

        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await RecalcProjectStatusAsync(db, task.ProjectId);
    }

    private static async System.Threading.Tasks.Task RecalcProjectStatusAsync(LocalDbContext db, Guid projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).ToListAsync();
        if (tasks.Count == 0)
            project.Status = ProjectStatus.Planning;
        else if (tasks.All(t => t.Status == TaskStatus.Completed))
            project.Status = ProjectStatus.Completed;
        else if (tasks.Any(t => t.Status == TaskStatus.InProgress || t.Status == TaskStatus.Paused || t.Status == TaskStatus.Completed))
            project.Status = ProjectStatus.InProgress;
        else
            project.Status = ProjectStatus.Planning;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async void MarkDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (_stage is null) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(_stage.Id);
        if (entity is null) return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _stage.IsMarkedForDeletion = entity.IsMarkedForDeletion;
        ApplyDeletionMark(entity.IsMarkedForDeletion);
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

        var stageOverlay = new StageDetailOverlay();
        stageOverlay.SetStage(item, _task, _onClosed);

        MainWindow.Instance?.ShowDrawer(taskPanel, stageOverlay, 850);
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
