using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

/// <summary>A single row in the Gantt chart (task).</summary>
public sealed class GanttTaskRow
{
    public LocalTask Task         { get; init; } = null!;
    /// <summary>0–1 fraction: gap before bar.</summary>
    public double BarLeft         { get; init; }
    /// <summary>0–1 fraction: bar width.</summary>
    public double BarWidth        { get; init; }
    /// <summary>0–1 fraction: gap after bar (= 1 - BarLeft - BarWidth).</summary>
    public double BarRemainder    { get; init; }
    public bool   HasBar          { get; init; }
    public string StatusLabel     { get; init; } = string.Empty;
    public string StatusColor     { get; init; } = "#6B778C";
    /// <summary>Bar colour based on task progress % (same palette as ProgressPercentToBrushConverter).</summary>
    public string BarColorHex     { get; init; } = "#EF4444";
}

/// <summary>A single row in the Gantt chart (stage).</summary>
public sealed class GanttStageRow
{
    public StageItem  Stage       { get; init; } = null!;
    public LocalTask? ParentTask  { get; init; }
    public double BarLeft         { get; init; }
    public double BarWidth        { get; init; }
    public double BarRemainder    { get; init; }
    public bool   HasBar          { get; init; }
    public string StatusLabel     { get; init; } = string.Empty;
    /// <summary>Badge colour — original stage status palette (gray/blue/green).</summary>
    public string StatusColor     { get; init; } = "#6B778C";
    /// <summary>Bar colour — matches task progress palette (red/blue/green).</summary>
    public string BarColorHex     { get; init; } = "#EF4444";
}

public partial class GanttViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;

    [ObservableProperty] private DateTime _currentDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private string _monthTitle    = string.Empty;
    [ObservableProperty] private string _activeTab     = "Tasks";

    [ObservableProperty] private ObservableCollection<GanttTaskRow>  _taskRows  = [];
    [ObservableProperty] private ObservableCollection<GanttStageRow> _stageRows = [];
    [ObservableProperty] private ObservableCollection<GanttDayHeader> _dayHeaders = [];

    /// <summary>0–1 fraction for the "today" vertical line (–1 = not in current month).</summary>
    [ObservableProperty] private double _todayFraction = -1;

    public GanttViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _auth      = auth;
        UpdateMonthTitle();
    }

    partial void OnCurrentDateChanged(DateTime value)
    {
        UpdateMonthTitle();
        _ = LoadAsync();
    }

    partial void OnActiveTabChanged(string value) => _ = LoadAsync();

    private void UpdateMonthTitle()
    {
        var ci  = new CultureInfo("ru-RU");
        var raw = CurrentDate.ToString("MMMM yyyy", ci);
        MonthTitle = raw.Length > 0 ? char.ToUpper(raw[0]) + raw[1..] : raw;
    }

    [RelayCommand] private void PreviousMonth() => CurrentDate = CurrentDate.AddMonths(-1);
    [RelayCommand] private void NextMonth()      => CurrentDate = CurrentDate.AddMonths(1);
    [RelayCommand] private void GoToToday()
        => CurrentDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var userId    = _auth.UserId;
            bool isAdmin  = _auth.UserRole is "Administrator" or "Admin";
            bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
            bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
            bool isWorker  = string.Equals(_auth.UserRole, "Worker",  StringComparison.OrdinalIgnoreCase);

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
                    var pids = await db.ProjectMembers
                        .Where(m => m.UserId == userId.Value).Select(m => m.ProjectId).ToListAsync();
                    taskQuery = taskQuery.Where(t => pids.Contains(t.ProjectId));
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

            var allTasks  = await taskQuery.OrderBy(t => t.DueDate).ThenBy(t => t.Name).ToListAsync();
            var taskIds   = allTasks.Select(t => t.Id).ToList();
            var allStages = taskIds.Count > 0
                ? await db.TaskStages.Where(s => !s.IsArchived && !s.IsMarkedForDeletion && taskIds.Contains(s.TaskId)).ToListAsync()
                : new List<LocalTaskStage>();

            foreach (var t in allTasks)
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

            // Build month range
            var start     = new DateTime(CurrentDate.Year, CurrentDate.Month, 1);
            var daysCount = DateTime.DaysInMonth(CurrentDate.Year, CurrentDate.Month);
            var end       = start.AddDays(daysCount - 1);
            var today     = DateTime.Today;
            double totalDays = daysCount;

            // Day headers
            var ci      = new CultureInfo("ru-RU");
            var headers = new List<GanttDayHeader>();
            for (int d = 0; d < daysCount; d++)
            {
                var day = start.AddDays(d);
                headers.Add(new GanttDayHeader
                {
                    DayNumber = day.Day.ToString(),
                    DayName   = day.ToString("ddd", ci),
                    IsToday   = day.Date == today.Date
                });
            }
            DayHeaders = new ObservableCollection<GanttDayHeader>(headers);

            // Today fraction
            TodayFraction = today >= start && today <= end
                ? ((today - start).TotalDays + 0.5) / totalDays
                : -1;

            var taskDict = allTasks.ToDictionary(t => t.Id);

            // Build task rows (полоса: от даты создания задачи до срока выполнения)
            var taskRows = allTasks.Select(t =>
            {
                TryComputeBarForRange(
                    DateOnlyFromCreatedAt(t.CreatedAt), t.DueDate,
                    start, end, totalDays,
                    out var hasBar, out var left, out var width);
                return new GanttTaskRow
                {
                    Task         = t,
                    HasBar       = hasBar,
                    BarLeft      = left,
                    BarWidth     = width,
                    BarRemainder = Math.Max(0.001, 1.0 - left - width),
                    StatusLabel  = TaskStatusLabel(t.Status),
                    StatusColor  = TaskStatusColor(t.Status),
                    BarColorHex  = ProgressToHex(t.ProgressPercent)
                };
            }).ToList();
            TaskRows = new ObservableCollection<GanttTaskRow>(taskRows);

            // Build stage rows
            var stageRows = stageList.Select(s =>
            {
                taskDict.TryGetValue(s.TaskId, out var parentTask);
                var item = new StageItem
                {
                    Stage       = s,
                    TaskId      = s.TaskId,
                    TaskName    = parentTask?.Name      ?? "—",
                    ProjectId   = parentTask?.ProjectId ?? Guid.Empty,
                    ProjectName = parentTask?.ProjectName ?? "—"
                };

                TryComputeBarForRange(
                    DateOnlyFromCreatedAt(s.CreatedAt), s.DueDate,
                    start, end, totalDays,
                    out var hasBar, out var left, out var width);
                return new GanttStageRow
                {
                    Stage        = item,
                    ParentTask   = parentTask,
                    HasBar       = hasBar,
                    BarLeft      = left,
                    BarWidth     = width,
                    BarRemainder = Math.Max(0.001, 1.0 - left - width),
                    StatusLabel  = StageStatusLabel(s.Status),
                    StatusColor  = StageStatusColor(s.Status),
                    BarColorHex  = StageBarColor(s.Status)
                };
            }).ToList();
            StageRows = new ObservableCollection<GanttStageRow>(stageRows);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Progress-based colour matching ProgressPercentToBrushConverter.</summary>
    private static string ProgressToHex(int pct) => pct >= 100 ? "#16A34A"
        : pct >= 60  ? "#0082FF"
        : pct >= 30  ? "#F97316"
        : "#EF4444";

    private static string TaskStatusLabel(TaskStatus s) => s switch
    {
        TaskStatus.Planned    => "Запланирована",
        TaskStatus.InProgress => "Выполняется",
        TaskStatus.Paused     => "Приостановлена",
        TaskStatus.Completed  => "Завершена",
        _                     => s.ToString()
    };

    // Colors matching TaskStatusToBrushConverter exactly
    private static string TaskStatusColor(TaskStatus s) => s switch
    {
        TaskStatus.Planned    => "#6B778C",
        TaskStatus.InProgress => "#0082FF",
        TaskStatus.Paused     => "#FF8B00",
        TaskStatus.Completed  => "#00875A",
        _                     => "#6B778C"
    };

    private static string StageStatusLabel(StageStatus s) => s switch
    {
        StageStatus.Planned    => "Запланирован",
        StageStatus.InProgress => "Выполняется",
        StageStatus.Completed  => "Завершён",
        _                      => s.ToString()
    };

    // Badge colour — original StageStatusToBrushConverter palette
    private static string StageStatusColor(StageStatus s) => s switch
    {
        StageStatus.Planned    => "#6B778C",
        StageStatus.InProgress => "#0082FF",
        StageStatus.Completed  => "#00875A",
        _                      => "#6B778C"
    };

    // Bar colour — same palette as task progress bars
    private static string StageBarColor(StageStatus s) => s switch
    {
        StageStatus.Planned    => "#EF4444",
        StageStatus.InProgress => "#0082FF",
        StageStatus.Completed  => "#16A34A",
        _                      => "#EF4444"
    };

    /// <summary>Календарная дата создания сущности для шкалы Ганта (UTC → локальная).</summary>
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
        var endDt   = endDate.ToDateTime(TimeOnly.MinValue);
        if (startDt > monthEnd || endDt < monthStart) return;

        var clipStart = startDt < monthStart ? monthStart : startDt;
        var clipEnd   = endDt > monthEnd ? monthEnd : endDt;
        left  = (clipStart - monthStart).TotalDays / totalDaysInMonth;
        width = Math.Max((clipEnd - clipStart).TotalDays + 1, 1) / totalDaysInMonth;
        hasBar = true;
    }
}

public sealed class GanttDayHeader
{
    public string DayNumber { get; init; } = string.Empty;
    public string DayName   { get; init; } = string.Empty;
    public bool   IsToday   { get; init; }
}
