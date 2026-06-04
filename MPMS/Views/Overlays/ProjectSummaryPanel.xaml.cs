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
using MPMS.Services;

namespace MPMS.Views.Overlays;

public partial class ProjectSummaryPanel : UserControl
{
    private int _loadVersion;
    private LocalProject? _project;

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
        _project = project;
        Visibility = Visibility.Visible;
        var loadVersion = ++_loadVersion;
        _ = LoadAsync(project, loadVersion);
    }

    private async System.Threading.Tasks.Task LoadAsync(LocalProject project, int loadVersion)
    {
        // Базовая информация
        ProjectNameText.Text = project.Name;
        ClientText.Text = project.Client ?? "—";
        AddressText.Text = project.Address ?? "—";

        // Даты
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

        var pct = project.ProgressPercent;
        ProgressText.Text = $"{pct}%";
        CompletedTasksText.Text = project.CompletedTasks.ToString();
        TotalTasksText.Text = project.TotalTasks.ToString();

        // Анимация заполнения прогресса асинхронно
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Обновить ширину заполнения после прохода компоновки
            ProgressFill.Loaded += (_, _) => UpdateProgressWidth(pct);
            UpdateProgressWidth(pct);
        });

        if (loadVersion != _loadVersion) return;

        var displayStatus = project.Status == ProjectStatus.Closed
            ? ProjectStatus.Closed
            : project.TotalTasks > 0 && project.CompletedTasks >= project.TotalTasks
            ? ProjectStatus.Completed
            : project.Status;

        // Статус
        var statusBrush = ProjectStatusToBrushConverter.Instance.Convert(
            displayStatus, typeof(Brush), null!, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = statusBrush ?? Brushes.Gray;
        StatusDot.Background = new SolidColorBrush(Colors.White) { Opacity = 0.7 };
        StatusText.Text = ProjectStatusToStringConverter.Instance.Convert(
            displayStatus, typeof(string), null!, CultureInfo.InvariantCulture) as string ?? "—";

        // Цвет шапки по статусу
        StatusHeaderBand.Background = displayStatus switch
        {
            ProjectStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            ProjectStatus.Completed => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
            ProjectStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xFE, 0xF2, 0xF2)),
            ProjectStatus.Closed => new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            _ => new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA))
        };

        // Менеджер - будет установлен позже через DataContext после загрузки участников
        // Временное отображение до загрузки
        ManagerNameTextStatic.Text = project.ManagerName ?? "—";
        ManagerInitialsTextStatic.Text = project.ManagerInitials;
        var managerBmp = AvatarHelper.GetImageSource(project.ManagerAvatarData, project.ManagerAvatarPath, project.ManagerName);
        if (managerBmp is not null)
        {
            ManagerAvatarImageStatic.Source = managerBmp;
            ManagerAvatarImageStatic.Visibility = Visibility.Visible;
            ManagerInitialsTextStatic.Visibility = Visibility.Collapsed;
        }
        else
        {
            ManagerAvatarImageStatic.Visibility = Visibility.Collapsed;
            ManagerInitialsTextStatic.Visibility = Visibility.Visible;
        }
        ManagerButton.Visibility = Visibility.Collapsed;

        // Загрузить участников проекта из БД
        try
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var members = await db.ProjectMembers
                .Where(m => m.ProjectId == project.Id)
                .OrderBy(m => m.UserRole)
                .ThenBy(m => m.UserName)
                .ToListAsync();

            var userIds = members.Select(m => m.UserId).Distinct().ToList();
            var roleMap = new Dictionary<Guid, string?>();
            var subRoleMap = new Dictionary<Guid, string?>();
            var addSpecMap = new Dictionary<Guid, string?>();
            if (userIds.Count > 0)
            {
                var userAvatars = await db.Users.Where(u => userIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.AvatarData, u.AvatarPath, u.SubRole, u.AdditionalSubRoles, u.RoleName })
                    .ToListAsync();
                var avDict = userAvatars.ToDictionary(u => u.Id);
                roleMap = userAvatars.ToDictionary(u => u.Id, u => (string?)u.RoleName);
                subRoleMap = userAvatars.ToDictionary(u => u.Id, u => (string?)u.SubRole);
                addSpecMap = userAvatars.ToDictionary(u => u.Id, u => u.AdditionalSubRoles);
                foreach (var m in members)
                {
                    if (avDict.TryGetValue(m.UserId, out var av))
                    {
                        m.AvatarData = av.AvatarData;
                        m.AvatarPath = av.AvatarPath;
                        m.SubRole = av.SubRole;
                        m.AdditionalSubRolesJson = av.AdditionalSubRoles;
                    }
                }
            }
            var auth = App.Services.GetRequiredService<IAuthService>();
            var foremanDisplayItems = members.Where(m => m.UserRole is "Foreman" or "Прораб")
                .Select(m =>
                {
                    var role = roleMap.TryGetValue(m.UserId, out var r) ? r : null;
                    var subRole = subRoleMap.TryGetValue(m.UserId, out var sr) ? sr : null;
                    var addSpec = addSpecMap.TryGetValue(m.UserId, out var aj) ? aj : null;
                    var peek = UserPeekAccess.CanInteractPeekRow(auth, db, role);
                    return new AssigneeDisplayItem(m.UserId, m.UserName, role, m.AvatarData, m.AvatarPath, subRole, addSpec, peek);
                })
                .ToList();
            var workerDisplayItems = members.Where(m => m.UserRole is "Worker" or "Работник")
                .Select(m =>
                {
                    var role = roleMap.TryGetValue(m.UserId, out var r) ? r : null;
                    var subRole = subRoleMap.TryGetValue(m.UserId, out var sr) ? sr : null;
                    var addSpec = addSpecMap.TryGetValue(m.UserId, out var aj) ? aj : null;
                    var peek = UserPeekAccess.CanInteractPeekRow(auth, db, role);
                    return new AssigneeDisplayItem(m.UserId, m.UserName, role, m.AvatarData, m.AvatarPath, subRole, addSpec, peek);
                })
                .ToList();
            // Менеджер - создаем AssigneeDisplayItem если есть ManagerId
            AssigneeDisplayItem? managerItem = null;
            if (project.ManagerId != Guid.Empty)
            {
                var managerRole = roleMap.TryGetValue(project.ManagerId, out var mr) ? mr : "Manager";
                var managerSubRole = subRoleMap.TryGetValue(project.ManagerId, out var msr) ? msr : null;
                var managerAddSpec = addSpecMap.TryGetValue(project.ManagerId, out var mas) ? mas : null;
                var managerPeek = UserPeekAccess.CanInteractPeekRow(auth, db, managerRole);
                managerItem = new AssigneeDisplayItem(
                    project.ManagerId,
                    project.ManagerName ?? "—",
                    managerRole,
                    project.ManagerAvatarData,
                    project.ManagerAvatarPath,
                    managerSubRole,
                    managerAddSpec,
                    managerPeek);
            }
            var foremans = foremanDisplayItems;
            var workers = workerDisplayItems;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (loadVersion != _loadVersion) return;
                // Менеджер - устанавливаем DataContext если есть managerItem
                if (managerItem != null)
                {
                    ManagerButton.DataContext = managerItem;
                    ManagerButton.Visibility = managerItem.IsUserPeekInteractive ? Visibility.Visible : Visibility.Collapsed;
                    // Скрываем статические элементы
                    ManagerAvatarStatic.Visibility = Visibility.Collapsed;
                    ManagerNameTextStatic.Visibility = Visibility.Collapsed;
                    ManagerInitialsTextStatic.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ManagerButton.Visibility = Visibility.Collapsed;
                }
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

        // Цвет по прогрессу
        ProgressFill.Background = pct >= 100
            ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))
            : pct >= 60
                ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
                : pct >= 30
                    ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
                    : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    }
    private void AssigneePeek_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneeDisplayItem item) return;
        MainWindow.Instance?.OpenUserPeekFromDrawer(item.UserId, _project.Id);
    }
}
