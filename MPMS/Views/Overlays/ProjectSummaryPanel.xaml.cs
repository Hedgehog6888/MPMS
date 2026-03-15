using System;
using System.Collections.Generic;
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

namespace MPMS.Views.Overlays;

public partial class ProjectSummaryPanel : UserControl
{
    public ProjectSummaryPanel()
    {
        InitializeComponent();
    }

    public void SetProject(LocalProject? project)
    {
        if (project is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        _ = LoadAsync(project);
    }

    private async System.Threading.Tasks.Task LoadAsync(LocalProject project)
    {
        // Basic info
        ProjectNameText.Text = project.Name;
        ClientText.Text = project.Client ?? "—";
        AddressText.Text = project.Address ?? "—";

        // Dates
        if (project.StartDate.HasValue && project.EndDate.HasValue)
        {
            DatesText.Text = $"{project.StartDate.Value:dd.MM.yyyy} – {project.EndDate.Value:dd.MM.yyyy}";
            var today = DateOnly.FromDateTime(DateTime.Today);
            var daysLeft = project.EndDate.Value.DayNumber - today.DayNumber;
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
            DatesText.Text = "Не указаны";
            DaysLeftText.Text = "";
        }

        // Progress bar (дробные % через ProgressCalculator)
        var pct = project.ProgressPercent;
        ProgressText.Text = $"{pct}%";
        CompletedTasksText.Text = project.CompletedTasks.ToString();
        TotalTasksText.Text = project.TotalTasks.ToString();

        // Animate progress fill asynchronously
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Update fill width after layout pass
            ProgressFill.Loaded += (_, _) => UpdateProgressWidth(pct);
            UpdateProgressWidth(pct);
        });

        // Status
        var statusBrush = ProjectStatusToBrushConverter.Instance.Convert(
            project.Status, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = statusBrush ?? Brushes.Gray;
        StatusDot.Background = new SolidColorBrush(Colors.White) { Opacity = 0.7 };
        StatusText.Text = ProjectStatusToStringConverter.Instance.Convert(
            project.Status, typeof(string), null, CultureInfo.InvariantCulture) as string ?? "—";

        // Header band color based on status
        StatusHeaderBand.Background = project.Status switch
        {
            ProjectStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            ProjectStatus.Completed  => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
            ProjectStatus.Cancelled  => new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2)),
            _                        => new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA))
        };

        // Manager
        ManagerInitialsText.Text = project.ManagerInitials;
        ManagerNameText.Text = project.ManagerName ?? "—";

        // Load project members from DB
        try
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var members = await db.ProjectMembers
                .Where(m => m.ProjectId == project.Id)
                .OrderBy(m => m.UserRole)
                .ThenBy(m => m.UserName)
                .ToListAsync();

            var foremans = members.Where(m => m.UserRole is "Foreman" or "Прораб").ToList();
            var workers  = members.Where(m => m.UserRole is "Worker" or "Работник").ToList();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ForemanSection.Visibility = foremans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ForemanList.ItemsSource = foremans;

                WorkersSection.Visibility = workers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                WorkersList.ItemsSource = workers;
            });
        }
        catch { /* offline: ignore */ }
    }

    private void UpdateProgressWidth(int pct)
    {
        var parent = ProgressFill.Parent as Border;
        if (parent is null) return;
        var available = parent.ActualWidth;
        if (available <= 0) available = 220;
        ProgressFill.Width = Math.Max(0, available * pct / 100.0);

        // Color by progress
        ProgressFill.Background = pct >= 100
            ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
            : pct >= 60
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF))
                : pct >= 30
                    ? new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16))
                    : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }
}
