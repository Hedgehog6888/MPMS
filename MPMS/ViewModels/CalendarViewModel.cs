using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

/// <summary>Одна плашка в ячейке календаря: задача или этап (с родительской задачей).</summary>
public sealed class CalendarChipItem
{
    public bool IsStage { get; init; }
    public LocalTask? Task { get; init; }
    public LocalTaskStage? Stage { get; init; }
    public LocalTask? StageParentTask { get; init; }
    public bool IsFromClosedProject { get; init; }
}

/// <summary>Этап в календаре с родительской задачей (дедлайн этапа может не совпадать с дедлайном задачи).</summary>
public sealed class CalendarDayStage
{
    public LocalTaskStage Stage { get; init; } = null!;
    public LocalTask ParentTask { get; init; } = null!;
    public bool IsFromClosedProject { get; init; }
}

/// <summary>Задача в оверлее дня календаря.</summary>
public sealed class CalendarDayTaskEntry
{
    public LocalTask Task { get; init; } = null!;
    public bool IsFromClosedProject { get; init; }
}

/// <summary>Данные для одной ячейки сетки календаря.</summary>
public sealed class CalendarCell
{
    public bool IsEmpty { get; init; }
    public DateTime Date { get; init; }
    public bool IsToday { get; init; }
    public List<LocalTask> Tasks { get; init; } = [];
    public List<CalendarDayStage> DayStages { get; init; } = [];
    /// <summary>Видимые плашки (число задаётся шириной ячейки); остальное — в <see cref="MoreCount"/>.</summary>
    public List<CalendarChipItem> DisplayChips { get; init; } = [];
    public int MoreCount { get; init; }
    public string MoreLabel => MoreCount > 0 ? $"+{MoreCount}" : "";
}

public partial class CalendarViewModel : ViewModelBase, ILoadable
{
    private const string ShowClosedProjectsSettingKey = "Calendar.ShowClosedProjects";

    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;

    private List<LocalTask>? _cachedAllTasks;
    private List<LocalTaskStage>? _cachedStagesForCalendar;
    private HashSet<Guid> _closedProjectIds = [];
    private int _maxVisibleChipsPerDay = 2;

    /// <summary>Сколько плашек показывать в ячейке (2–4), по ширине области календаря.</summary>
    public int MaxVisibleChipsPerDay
    {
        get => _maxVisibleChipsPerDay;
        set
        {
            var v = Math.Clamp(value, 2, 4);
            if (v == _maxVisibleChipsPerDay) return;
            _maxVisibleChipsPerDay = v;
            OnPropertyChanged(nameof(MaxVisibleChipsPerDay));
            if (_cachedAllTasks is not null && _cachedStagesForCalendar is not null)
                BuildCells(_cachedAllTasks, _cachedStagesForCalendar);
        }
    }

    [ObservableProperty] private DateTime _currentDate = DateTime.Today;
    [ObservableProperty] private ObservableCollection<CalendarCell> _calendarCells = [];
    [ObservableProperty] private string _monthTitle = string.Empty;
    [ObservableProperty] private bool _showClosedProjects;

    public IReadOnlySet<Guid> ClosedProjectIds => _closedProjectIds;

    public CalendarViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth)
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
    [RelayCommand] private void GoToToday() => CurrentDate = DateTime.Today;

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

            var taskQuery = db.Tasks.Where(t => !t.IsArchived && !t.IsMarkedForDeletion && t.DueDate != null);

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

            var allTasks = await taskQuery.ToListAsync();
            _closedProjectIds = (await db.Projects
                .Where(p => p.IsClosed)
                .Select(p => p.Id)
                .ToListAsync())
                .ToHashSet();

            var openTasks = allTasks.Where(t => !_closedProjectIds.Contains(t.ProjectId)).ToList();
            var closedTasks = ShowClosedProjects
                ? allTasks.Where(t => _closedProjectIds.Contains(t.ProjectId)).ToList()
                : [];
            var orderedTasks = openTasks.Concat(closedTasks).ToList();

            var taskIds = orderedTasks.Select(t => t.Id).ToList();
            List<LocalTaskStage> allStagesForTasks = [];
            if (taskIds.Count > 0)
            {
                allStagesForTasks = await db.TaskStages
                    .Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived).ToListAsync();
                foreach (var t in orderedTasks)
                    ProgressCalculator.ApplyTaskMetrics(t, allStagesForTasks.Where(s => s.TaskId == t.Id).ToList());
            }

            var stagesForCalendar = allStagesForTasks
                .Where(s => !s.IsMarkedForDeletion && s.DueDate != null)
                .ToList();

            _cachedAllTasks = orderedTasks;
            _cachedStagesForCalendar = stagesForCalendar;
            BuildCells(orderedTasks, stagesForCalendar);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildCells(List<LocalTask> allTasks, List<LocalTaskStage> stagesWithDueDate)
    {
        var year = CurrentDate.Year;
        var month = CurrentDate.Month;
        var monthStart = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var today = DateTime.Today;
        var taskById = allTasks.ToDictionary(t => t.Id);

        var dow = (int)monthStart.DayOfWeek;
        var empty = dow == 0 ? 6 : dow - 1;

        var cells = new List<CalendarCell>();
        for (int i = 0; i < empty; i++)
            cells.Add(new CalendarCell { IsEmpty = true, Date = monthStart.AddDays(-(empty - i)) });

        for (int d = 1; d <= daysInMonth; d++)
        {
            var date = new DateTime(year, month, d);
            var dateOnly = DateOnly.FromDateTime(date);
            var openDayTasks = allTasks
                .Where(t => t.DueDate == dateOnly && !_closedProjectIds.Contains(t.ProjectId))
                .OrderBy(t => t.Status)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var closedDayTasks = allTasks
                .Where(t => t.DueDate == dateOnly && _closedProjectIds.Contains(t.ProjectId))
                .OrderBy(t => t.Status)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var openDayStages = stagesWithDueDate
                .Where(s => s.DueDate == dateOnly && taskById.ContainsKey(s.TaskId) && !_closedProjectIds.Contains(taskById[s.TaskId].ProjectId))
                .OrderBy(s => s.Status)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(s =>
                {
                    var parent = taskById[s.TaskId];
                    return new CalendarDayStage { Stage = s, ParentTask = parent, IsFromClosedProject = false };
                })
                .ToList();
            var closedDayStages = stagesWithDueDate
                .Where(s => s.DueDate == dateOnly && taskById.ContainsKey(s.TaskId) && _closedProjectIds.Contains(taskById[s.TaskId].ProjectId))
                .OrderBy(s => s.Status)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(s =>
                {
                    var parent = taskById[s.TaskId];
                    return new CalendarDayStage { Stage = s, ParentTask = parent, IsFromClosedProject = true };
                })
                .ToList();
            var dayTasks = openDayTasks.Concat(closedDayTasks).ToList();
            var dayStages = openDayStages.Concat(closedDayStages).ToList();

            var chips = new List<CalendarChipItem>();
            foreach (var t in openDayTasks)
            {
                chips.Add(new CalendarChipItem
                {
                    IsStage = false,
                    Task = t,
                    IsFromClosedProject = false
                });
            }
            foreach (var ds in openDayStages)
            {
                chips.Add(new CalendarChipItem
                {
                    IsStage = true,
                    Stage = ds.Stage,
                    StageParentTask = ds.ParentTask,
                    IsFromClosedProject = false
                });
            }
            foreach (var t in closedDayTasks)
            {
                chips.Add(new CalendarChipItem
                {
                    IsStage = false,
                    Task = t,
                    IsFromClosedProject = true
                });
            }
            foreach (var ds in closedDayStages)
            {
                chips.Add(new CalendarChipItem
                {
                    IsStage = true,
                    Stage = ds.Stage,
                    StageParentTask = ds.ParentTask,
                    IsFromClosedProject = true
                });
            }

            var cap = MaxVisibleChipsPerDay;
            var more = Math.Max(0, chips.Count - cap);
            var display = chips.Take(cap).ToList();

            cells.Add(new CalendarCell
            {
                Date = date,
                IsToday = date.Date == today.Date,
                Tasks = dayTasks,
                DayStages = dayStages,
                DisplayChips = display,
                MoreCount = more
            });
        }

        var rem = (7 - cells.Count % 7) % 7;
        for (int i = 1; i <= rem; i++)
            cells.Add(new CalendarCell { IsEmpty = true, Date = monthStart.AddMonths(1).AddDays(i - 1) });

        CalendarCells = new ObservableCollection<CalendarCell>(cells);
    }
}
