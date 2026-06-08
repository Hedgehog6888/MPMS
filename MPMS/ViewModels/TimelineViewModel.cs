using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

/// <summary>Одна строка в диаграмме Timeline (задача).</summary>
public sealed class TimelineTaskRow
{
    public LocalTask Task { get; init; } = null!;
    public double BarLeft { get; init; }
    public double BarWidth { get; init; }
    public double BarRemainder { get; init; }
    public bool HasBar { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string StatusColor { get; init; } = "#64748B";
    public string BarColorHex { get; init; } = "#EF4444";
    public string BarRangeLabel { get; init; } = "";
    public bool IsOverdue { get; init; }
    public bool IsFromClosedProject { get; init; }
}

/// <summary>Одна строка в диаграмме Timeline (этап).</summary>
public sealed class TimelineStageRow
{
    public StageItem Stage { get; init; } = null!;
    public LocalTask? ParentTask { get; init; }
    public double BarLeft { get; init; }
    public double BarWidth { get; init; }
    public double BarRemainder { get; init; }
    public bool HasBar { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public string StatusColor { get; init; } = "#64748B";
    public string BarColorHex { get; init; } = "#EF4444";
    public string BarRangeLabel { get; init; } = "";
    public bool IsOverdue { get; init; }
    public bool IsFromClosedProject { get; init; }
}

public partial class TimelineViewModel : ViewModelBase, ILoadable
{
    private const string ClosedProjectBarColor = "#000000";
    private const string ShowClosedProjectsSettingKey = "Timeline.ShowClosedProjects";

    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;

    [ObservableProperty] private DateTime _currentDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string _monthTitle = string.Empty;
    [ObservableProperty] private string _activeTab = "Tasks";
    [ObservableProperty] private bool _showClosedProjects;

    [ObservableProperty] private ObservableCollection<TimelineTaskRow> _taskRows = [];
    [ObservableProperty] private ObservableCollection<TimelineStageRow> _stageRows = [];
    [ObservableProperty] private ObservableCollection<TimelineDayHeader> _dayHeaders = [];

    /// <summary>Дробь 0–1 для вертикальной линии «сегодня» (–1 = не в текущем месяце).</summary>
    [ObservableProperty] private double _todayFraction = -1;

    public TimelineViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _showClosedProjects = LocalSettings.GetBool(ShowClosedProjectsSettingKey, defaultValue: false);
        UpdateMonthTitle();
    }

    partial void OnCurrentDateChanged(DateTime value)
    {
        UpdateMonthTitle();
        _ = LoadAsync();
    }

    partial void OnActiveTabChanged(string value) => _ = LoadAsync();

    partial void OnShowClosedProjectsChanged(bool value)
    {
        LocalSettings.SetBool(ShowClosedProjectsSettingKey, value);
        _ = LoadAsync();
    }

    private void UpdateMonthTitle()
    {
        var ci = new CultureInfo("ru-RU");
        var raw = CurrentDate.ToString("MMMM yyyy", ci);
        MonthTitle = raw.Length > 0 ? char.ToUpper(raw[0]) + raw[1..] : raw;
    }

    [RelayCommand] private void PreviousMonth() => CurrentDate = CurrentDate.AddMonths(-1);
    [RelayCommand] private void NextMonth() => CurrentDate = CurrentDate.AddMonths(1);
    [RelayCommand]
    private void GoToToday()
        => CurrentDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var userId = _auth.UserId;
            bool isAdmin = _auth.UserRole is "Administrator" or "Admin";
            bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
            bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
            bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

            var taskQuery = db.Tasks.Where(t => !t.IsArchived && !t.IsMarkedForDeletion);

            if (userId.HasValue && !isAdmin)
            {
                if (isManager)
                {
                    taskQuery = taskQuery.Where(t =>
                        db.Projects.Any(p => p.Id == t.ProjectId && p.ManagerId == userId.Value));
                }
                else if (isForeman)
                {
                    var direct = await db.Tasks.Where(t => t.AssignedUserId == userId.Value)
                        .Select(t => t.Id).ToListAsync();
                    var via = await db.TaskAssignees.Where(a => a.UserId == userId.Value)
                        .Select(a => a.TaskId).ToListAsync();
                    var ids = direct.Concat(via).Distinct().ToList();
                    taskQuery = taskQuery.Where(t => ids.Contains(t.Id));
                }
                else if (isWorker)
                {
                    var direct = await db.Tasks.Where(t => t.AssignedUserId == userId.Value)
                        .Select(t => t.Id).ToListAsync();
                    var via = await db.TaskAssignees.Where(a => a.UserId == userId.Value)
                        .Select(a => a.TaskId).ToListAsync();
                    var ids = direct.Concat(via).Distinct().ToList();
                    taskQuery = taskQuery.Where(t => ids.Contains(t.Id));
                }
            }

            var allTasks = await taskQuery.ToListAsync();
            var closedProjectIds = (await db.Projects
                .Where(p => p.IsClosed)
                .Select(p => p.Id)
                .ToListAsync())
                .ToHashSet();
            var openTasks = allTasks.Where(t => !closedProjectIds.Contains(t.ProjectId)).ToList();
            var closedTasks = ShowClosedProjects
                ? allTasks.Where(t => closedProjectIds.Contains(t.ProjectId)).ToList()
                : [];
            var orderedTasks = openTasks.Concat(closedTasks).ToList();

            var taskIds = orderedTasks.Select(t => t.Id).ToList();
            var allStages = taskIds.Count > 0
                ? await db.TaskStages.Where(s => !s.IsArchived && !s.IsMarkedForDeletion && taskIds.Contains(s.TaskId)).ToListAsync()
                : new List<LocalTaskStage>();

            foreach (var t in orderedTasks)
                ProgressCalculator.ApplyTaskMetrics(t, allStages.Where(s => s.TaskId == t.Id).ToList());

            var stageList = allStages.ToList();
            if (userId.HasValue && isWorker)
            {
                var wsIds1 = await db.StageAssignees.Where(sa => sa.UserId == userId.Value)
                    .Select(sa => sa.StageId).ToListAsync();
                var wsIds2 = await db.TaskStages.Where(s => s.AssignedUserId == userId.Value)
                    .Select(s => s.Id).ToListAsync();
                var allWs = wsIds1.Concat(wsIds2).Distinct().ToList();
                stageList = stageList.Where(s => allWs.Contains(s.Id)).ToList();
            }

            // Формируем диапазон месяца
            var start = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
            var daysCount = DateTime.DaysInMonth(CurrentDate.Year, CurrentDate.Month);
            var end = start.AddDays(daysCount - 1);
            var today = DateTime.Today;
            double totalDays = daysCount;

            // Заголовки дней
            var ci = new CultureInfo("ru-RU");
            var headers = new List<TimelineDayHeader>();
            for (int d = 0; d < daysCount; d++)
            {
                var day = start.AddDays(d);
                headers.Add(new TimelineDayHeader
                {
                    DayNumber = day.Day.ToString(),
                    DayName = day.ToString("ddd", ci),
                    IsToday = day.Date == today.Date
                });
            }
            DayHeaders = new ObservableCollection<TimelineDayHeader>(headers);

            // Доля для «сегодня»
            TodayFraction = today >= start && today <= end
                ? ((today - start).TotalDays + 0.5) / totalDays
                : -1;

            var taskDict = orderedTasks.ToDictionary(t => t.Id);

            // Формируем строки задач (полоса: от даты создания до срока); только пересечение с выбранным месяцем
            var taskRows = orderedTasks
                .Select(t =>
                {
                    var fromClosed = closedProjectIds.Contains(t.ProjectId);
                    var barStart = DateOnlyFromCreatedAt(t.CreatedAt);
                    TryComputeBarForRange(
                        barStart, t.DueDate,
                        start, end, totalDays,
                        out var hasBar, out var left, out var width);
                    return new TimelineTaskRow
                    {
                        Task = t,
                        HasBar = hasBar,
                        BarLeft = left,
                        BarWidth = width,
                        BarRemainder = Math.Max(0.001, 1.0 - left - width),
                        StatusLabel = TaskStatusLabel(t.Status),
                        StatusColor = TaskStatusColor(t.Status),
                        BarColorHex = fromClosed ? ClosedProjectBarColor : ProgressToHex(t.ProgressPercent),
                        BarRangeLabel = FormatTimelineBarRangeLabel(barStart, t.DueDate),
                        IsOverdue = t.IsOverdue,
                        IsFromClosedProject = fromClosed
                    };
                })
                .Where(r => r.HasBar)
                .OrderBy(r => r.IsFromClosedProject)
                .ThenBy(r => r.Task.CreatedAt)
                .ThenBy(r => r.Task.Name)
                .ToList();
            TaskRows = new ObservableCollection<TimelineTaskRow>(taskRows);

            // Формируем строки этапов — только если полоса попадает в месяц; порядок по дате создания этапа
            var stageRows = stageList
                .Select(s =>
                {
                    taskDict.TryGetValue(s.TaskId, out var parentTask);
                    var fromClosed = parentTask is not null && closedProjectIds.Contains(parentTask.ProjectId);
                    var item = new StageItem
                    {
                        Stage = s,
                        TaskId = s.TaskId,
                        TaskName = parentTask?.Name ?? "—",
                        ProjectId = parentTask?.ProjectId ?? Guid.Empty,
                        ProjectName = parentTask?.ProjectName ?? "—"
                    };

                    var stageBarStart = DateOnlyFromCreatedAt(s.CreatedAt);
                    TryComputeBarForRange(
                        stageBarStart, s.DueDate,
                        start, end, totalDays,
                        out var hasBar, out var left, out var width);
                    return new TimelineStageRow
                    {
                        Stage = item,
                        ParentTask = parentTask,
                        HasBar = hasBar,
                        BarLeft = left,
                        BarWidth = width,
                        BarRemainder = Math.Max(0.001, 1.0 - left - width),
                        StatusLabel = StageStatusLabel(s.Status),
                        StatusColor = StageStatusColor(s.Status),
                        BarColorHex = fromClosed ? ClosedProjectBarColor : StageBarColor(s.Status),
                        BarRangeLabel = FormatTimelineBarRangeLabel(stageBarStart, s.DueDate),
                        IsOverdue = s.IsOverdue,
                        IsFromClosedProject = fromClosed
                    };
                })
                .Where(r => r.HasBar)
                .OrderBy(r => r.IsFromClosedProject)
                .ThenBy(r => r.Stage.Stage.CreatedAt)
                .ThenBy(r => r.Stage.Stage.Name)
                .ToList();
            StageRows = new ObservableCollection<TimelineStageRow>(stageRows);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Цвет на основе прогресса, совпадающий с ProgressPercentToBrushConverter.</summary>
    private static string ProgressToHex(int pct) => pct >= 100 ? "#10B981"
        : pct >= 60 ? "#3B82F6"
        : pct >= 30 ? "#F59E0B"
        : "#EF4444";

    private static string TaskStatusLabel(TaskStatus s) => s switch
    {
        TaskStatus.Planned => "Запланирована",
        TaskStatus.InProgress => "Выполняется",
        TaskStatus.Paused => "Приостановлена",
        TaskStatus.Completed => "Завершена",
        _ => s.ToString()
    };

    // Цвета, точно совпадающие с TaskStatusToBrushConverter
    private static string TaskStatusColor(TaskStatus s) => s switch
    {
        TaskStatus.Planned => "#64748B",
        TaskStatus.InProgress => "#3B82F6",
        TaskStatus.Paused => "#F59E0B",
        TaskStatus.Completed => "#10B981",
        _ => "#64748B"
    };

    private static string StageStatusLabel(StageStatus s) => s switch
    {
        StageStatus.Planned => "Запланирован",
        StageStatus.InProgress => "Выполняется",
        StageStatus.Completed => "Завершён",
        _ => s.ToString()
    };

    // Цвет бейджа — исходная палитра StageStatusToBrushConverter
    private static string StageStatusColor(StageStatus s) => s switch
    {
        StageStatus.Planned => "#64748B",
        StageStatus.InProgress => "#3B82F6",
        StageStatus.Completed => "#10B981",
        _ => "#64748B"
    };

    // Цвет полосы — та же палитра, что у полос прогресса задач
    private static string StageBarColor(StageStatus s) => s switch
    {
        StageStatus.Planned => "#EF4444",
        StageStatus.InProgress => "#3B82F6",
        StageStatus.Completed => "#10B981",
        _ => "#EF4444"
    };

    /// <summary>Текст «с — по» для подсказки на полосе (та же логика, что у отрезка на шкале).</summary>
    private static string FormatTimelineBarRangeLabel(DateOnly barStart, DateOnly? dueDate)
    {
        if (!dueDate.HasValue) return "";
        var endDate = dueDate.Value;
        var startDate = barStart;
        if (endDate < startDate)
            (startDate, endDate) = (endDate, startDate);
        return $"{startDate:dd.MM.yyyy} — {endDate:dd.MM.yyyy}";
    }

    /// <summary>Календарная дата создания сущности для шкалы Таймлайна (UTC → локальная).</summary>
    private static DateOnly DateOnlyFromCreatedAt(DateTime createdAt)
    {
        var dt = createdAt.Kind == DateTimeKind.Utc
            ? createdAt.ToLocalTime()
            : createdAt;
        return DateOnly.FromDateTime(dt);
    }

    /// <summary>Полоса от barStart до due в пределах видимого месяца.</summary>
    private static void TryComputeBarForRange(
        DateOnly barStart, DateOnly? dueDate,
        DateTime monthStart, DateTime monthEnd, double totalDaysInMonth,
        out bool hasBar, out double left, out double width)
    {
        left = width = 0;
        hasBar = false;
        if (!dueDate.HasValue) return;

        var endDate = dueDate.Value;
        var startDate = barStart;
        if (endDate < startDate)
            (startDate, endDate) = (endDate, startDate);

        var startDt = startDate.ToDateTime(TimeOnly.MinValue);
        var endDt = endDate.ToDateTime(TimeOnly.MinValue);
        if (startDt > monthEnd || endDt < monthStart) return;

        var clipStart = startDt < monthStart ? monthStart : startDt;
        var clipEnd = endDt > monthEnd ? monthEnd : endDt;
        left = (clipStart - monthStart).TotalDays / totalDaysInMonth;
        width = Math.Max((clipEnd - clipStart).TotalDays + 1, 1) / totalDaysInMonth;
        hasBar = true;
    }
}

public sealed class TimelineDayHeader
{
    public string DayNumber { get; init; } = string.Empty;
    public string DayName { get; init; } = string.Empty;
    public bool IsToday { get; init; }
}
