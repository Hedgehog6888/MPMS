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
        AssigneeText.Text = task.AssignedUserName ?? "—";
        ApplyAssigneeAvatar(task);
        DueDateText.Text = task.DueDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "—";

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

    private void ApplyAssigneeAvatar(LocalTask task)
    {
        AssigneeAvatarInitials.Text = task.AssignedUserInitials;
        AssigneeAvatarBorder.Background = new InitialsToBrushConverter().Convert(
            task.AssignedUserInitials, typeof(Brush), null!, CultureInfo.InvariantCulture) as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2));

        var avatar = AvatarHelper.GetImageSource(task.AssignedUserAvatarData, task.AssignedUserAvatarPath, task.AssignedUserName);
        if (avatar is not null)
        {
            AssigneeAvatarImage.Source = avatar;
            AssigneeAvatarImage.Visibility = Visibility.Visible;
            AssigneeAvatarInitials.Visibility = Visibility.Collapsed;
            return;
        }

        AssigneeAvatarImage.Source = null;
        AssigneeAvatarImage.Visibility = Visibility.Collapsed;
        AssigneeAvatarInitials.Visibility = Visibility.Visible;

        if (!task.AssignedUserId.HasValue)
            return;

        var loadVersion = ++_avatarLoadVersion;
        _ = LoadAssigneeAvatarAsync(task.AssignedUserId.Value, task.AssignedUserName, loadVersion);
    }

    private async Task LoadAssigneeAvatarAsync(Guid userId, string? userName, int loadVersion)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var avatar = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.AvatarData, u.AvatarPath })
            .FirstOrDefaultAsync();
        if (avatar is null || loadVersion != _avatarLoadVersion)
            return;

        var source = AvatarHelper.GetImageSource(avatar.AvatarData, avatar.AvatarPath, userName);
        if (source is null)
            return;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (loadVersion != _avatarLoadVersion)
                return;
            AssigneeAvatarImage.Source = source;
            AssigneeAvatarImage.Visibility = Visibility.Visible;
            AssigneeAvatarInitials.Visibility = Visibility.Collapsed;
        });
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
