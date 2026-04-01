using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class TaskSummaryPanel : UserControl
{
    private int _avatarLoadVersion;
    private int _metricsLoadVersion;
    private int _currentProgressPercent;
    private Guid _peekProjectId;

    public TaskSummaryPanel()
    {
        InitializeComponent();
        ProgressTrack.SizeChanged += (_, _) => UpdateProgressWidth(_currentProgressPercent);
    }

    public void SetTask(LocalTask? task)
    {
        SetTaskInternal(task, refreshMetrics: true);
    }

    private void SetTaskInternal(LocalTask? task, bool refreshMetrics)
    {
        if (task is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        _peekProjectId = task.ProjectId;
        Visibility = Visibility.Visible;
        TaskNameText.Text = task.Name ?? "—";
        DueDateText.Text = task.DueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
        var loadVersion = ++_avatarLoadVersion;
        _ = LoadAssigneesAsync(task, loadVersion);
        if (refreshMetrics)
        {
            var metricsVersion = ++_metricsLoadVersion;
            _ = RefreshTaskMetricsAsync(task.Id, metricsVersion);
        }

        // Days left / overdue
        if (task.DueDate.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var daysLeft = task.DueDate.Value.DayNumber - today.DayNumber;
            if (daysLeft > 0)
                DaysLeftText.Text = $"Осталось {daysLeft} дн.";
            else if (daysLeft == 0)
                DaysLeftText.Text = "Срок сегодня";
            else
                DaysLeftText.Text = $"Просрочен на {-daysLeft} дн.";

            DaysLeftText.Foreground = daysLeft < 0
                ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                : daysLeft <= 7
                    ? new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06))
                    : new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));
        }
        else
        {
            DaysLeftText.Text = "";
        }

        // Progress
        var pct = Math.Clamp(task.ProgressPercent, 0, 100);
        _currentProgressPercent = pct;
        ProgressText.Text = $"{_currentProgressPercent}%";
        CompletedStagesText.Text = task.CompletedStages.ToString();
        TotalStagesText.Text = task.TotalStages.ToString();
        UpdateProgressWidth(_currentProgressPercent);

        // Progress fill color by percent
        ProgressFill.Background = _currentProgressPercent >= 100
            ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
            : _currentProgressPercent >= 60
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF))
                : _currentProgressPercent >= 30
                    ? new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16))
                    : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

        // For the summary panel, treat 100% stage completion as completed even if the task entity
        // in the current UI flow has not refreshed its Status field yet.
        var displayStatus = task.TotalStages > 0 && task.CompletedStages >= task.TotalStages
            ? TaskStatus.Completed
            : task.Status;

        // Status
        var statusBrush = TaskStatusToBrushConverter.Instance.Convert(
            displayStatus, typeof(Brush), null!, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = statusBrush ?? Brushes.Gray;
        StatusDot.Background = new SolidColorBrush(Colors.White) { Opacity = 0.7 };
        StatusText.Text = TaskStatusToStringConverter.Instance.Convert(
            displayStatus, typeof(string), null!, CultureInfo.InvariantCulture) as string ?? "—";

        // Header band color based on status
        StatusHeaderBand.Background = displayStatus switch
        {
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            TaskStatus.Completed  => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
            TaskStatus.Paused     => new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB)),
            _                     => new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA))
        };

        // Project section (show when project name available)
        if (!string.IsNullOrWhiteSpace(task.ProjectName))
        {
            ProjectSection.Visibility = Visibility.Visible;
            ProjectNameText.Text = task.ProjectName;
        }
        else
        {
            ProjectSection.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RefreshTaskMetricsAsync(Guid taskId, int metricsVersion)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var taskEntity = await db.Tasks.FindAsync(taskId);
        if (taskEntity is null) return;

        await ProgressCalculator.ApplyTaskMetricsForTaskAsync(db, taskEntity);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (metricsVersion != _metricsLoadVersion)
                return;
            SetTaskInternal(taskEntity, refreshMetrics: false);
        });
    }

    private async Task LoadAssigneesAsync(LocalTask task, int loadVersion)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var assignees = await db.TaskAssignees
            .Where(a => a.TaskId == task.Id)
            .OrderBy(a => a.UserName)
            .ToListAsync();

        if (assignees.Count == 0 && task.AssignedUserId.HasValue)
        {
            assignees.Add(new LocalTaskAssignee
            {
                TaskId = task.Id,
                UserId = task.AssignedUserId.Value,
                UserName = task.AssignedUserName ?? "—"
            });
        }

        var userIds = assignees.Select(a => a.UserId).Distinct().ToList();
        var roles = new Dictionary<Guid, string?>();
        if (userIds.Count > 0)
        {
            var userRows = await db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath, u.RoleName, u.SubRole, u.AdditionalSubRoles })
                .ToListAsync();
            roles = userRows.ToDictionary(u => u.Id, u => (string?)u.RoleName);
            var byId = userRows.ToDictionary(u => u.Id);
            foreach (var a in assignees)
            {
                if (!byId.TryGetValue(a.UserId, out var u))
                    continue;
                a.AvatarData = u.AvatarData;
                a.AvatarPath = u.AvatarPath;
                a.RoleName   = u.RoleName;
                a.SubRole               = u.SubRole;
                a.AdditionalSubRolesJson = u.AdditionalSubRoles;
                if ((a.AvatarData is null || a.AvatarData.Length == 0)
                    && !string.IsNullOrWhiteSpace(a.AvatarPath))
                {
                    var fromFile = AvatarHelper.FileToBytes(a.AvatarPath);
                    if (fromFile is { Length: > 0 })
                        a.AvatarData = fromFile;
                }
            }
        }

        foreach (var a in assignees)
            a.IsUserPeekInteractive = UserPeekAccess.CanInteractPeekRow(auth, db, a.RoleName);

        static bool IsForemanRole(string? role) => role is "Foreman" or "Прораб";

        var foremen = assignees
            .Where(a => roles.TryGetValue(a.UserId, out var role) && IsForemanRole(role))
            .OrderBy(a => a.UserName)
            .ToList();
        var workers = assignees
            .Where(a => !roles.TryGetValue(a.UserId, out var role) || !IsForemanRole(role))
            .OrderBy(a => a.UserName)
            .ToList();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (loadVersion != _avatarLoadVersion)
                return;
            ForemanSection.Visibility = foremen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ForemanList.ItemsSource = foremen;
            WorkersSection.Visibility = workers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            WorkersList.ItemsSource = workers;
            NoAssigneesText.Visibility = foremen.Count == 0 && workers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void AssigneePeek_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalTaskAssignee ta) return;
        MainWindow.Instance?.HideAllOverlays();
        MainWindow.Instance?.TryOpenUserPeek(ta.UserId, _peekProjectId);
    }

    private void UpdateProgressWidth(int pct)
    {
        var available = ProgressTrack.ActualWidth;
        if (available <= 0) available = 220;
        ProgressFill.Width = Math.Max(0, available * pct / 100.0);
    }
}
