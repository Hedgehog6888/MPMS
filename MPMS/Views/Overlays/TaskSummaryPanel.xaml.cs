using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class TaskSummaryPanel : UserControl
{
    private int _avatarLoadVersion;

    public TaskSummaryPanel()
    {
        InitializeComponent();
    }

    public void SetTask(LocalTask? task)
    {
        if (task is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        TaskNameText.Text = task.Name;
        DueDateText.Text = task.DueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";
        var loadVersion = ++_avatarLoadVersion;
        _ = LoadAssigneesAsync(task, loadVersion);

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
        var pct = task.ProgressPercent;
        ProgressText.Text = $"{pct}%";
        CompletedStagesText.Text = task.CompletedStages.ToString();
        TotalStagesText.Text = task.TotalStages.ToString();

        _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressFill.Loaded += (_, _) => UpdateProgressWidth(pct);
            UpdateProgressWidth(pct);
        });

        // Progress fill color by percent
        ProgressFill.Background = pct >= 100
            ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
            : pct >= 60
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF))
                : pct >= 30
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

    private async Task LoadAssigneesAsync(LocalTask task, int loadVersion)
    {
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
        var roles = userIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (string?)u.RoleName);

        var foremen = assignees
            .Where(a => roles.TryGetValue(a.UserId, out var role) && role is "Foreman" or "Прораб")
            .Select(a => a.UserName)
            .Distinct()
            .ToList();
        var workers = assignees
            .Where(a => !roles.TryGetValue(a.UserId, out var role) || role is "Worker" or "Работник")
            .Select(a => a.UserName)
            .Distinct()
            .ToList();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (loadVersion != _avatarLoadVersion)
                return;
            ApplyAssigneeLists(ForemenPanel, ForemenSection, foremen, Color.FromRgb(0xEC, 0xFD, 0xF5), Color.FromRgb(0x16, 0x65, 0x34));
            ApplyAssigneeLists(WorkersPanel, WorkersSection, workers, Color.FromRgb(0xFF, 0xF7, 0xED), Color.FromRgb(0x92, 0x40, 0x0E));
            NoAssigneesText.Visibility = foremen.Count == 0 && workers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private static void ApplyAssigneeLists(WrapPanel panel, FrameworkElement section, IReadOnlyCollection<string> names, Color background, Color foreground)
    {
        panel.Children.Clear();
        section.Visibility = names.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var name in names)
        {
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(background),
                Child = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(foreground)
                }
            });
        }
    }

    private void UpdateProgressWidth(int pct)
    {
        var parent = ProgressFill.Parent as Border;
        if (parent is null) return;
        var available = parent.ActualWidth;
        if (available <= 0) available = 220;
        ProgressFill.Width = Math.Max(0, available * pct / 100.0);
    }
}
