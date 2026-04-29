using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System.Windows.Threading;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class HomeViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly ISyncService _sync;


    [ObservableProperty] private ObservableCollection<LocalActivityLog> _recentActivities = [];
    
    [ObservableProperty] private ObservableCollection<LocalNote> _notes = [];
    [ObservableProperty] private LocalNote? _selectedNote;
    [ObservableProperty] private bool _isNoteEditing;
    [ObservableProperty] private string _currentNoteXaml = string.Empty;
    [ObservableProperty] private bool _isBackConfirmPopupOpen;
    
    [ObservableProperty] private int _activeProjectsCount;
    [ObservableProperty] private int _totalProjectsCount;
    [ObservableProperty] private int _tasksDueTodayCount;
    [ObservableProperty] private int _tasksCompletedTodayCount;
    [ObservableProperty] private int _overdueTasksCount;
    [ObservableProperty] private int _totalFilesCount;
    
    // Role-specific card properties
    [ObservableProperty] private string _card2Title = "Ближайшее";
    [ObservableProperty] private string _card2Value = "—";
    [ObservableProperty] private string _card2SubValue = "Нет активных задач";
    
    [ObservableProperty] private string _card3Title = "Внимание";
    [ObservableProperty] private int _card3Value = 0;
    [ObservableProperty] private string _card3SubValue = "Все по графику";
    
    [ObservableProperty] private string _card1Title = "Эффективность";
    [ObservableProperty] private string _card1Value = "0%";
    [ObservableProperty] private string _card1SubValue = "за 7 дней";
    
    [ObservableProperty] private string _card4Title = "Статус системы";
    [ObservableProperty] private string _card4Value = "Загрузка...";
    [ObservableProperty] private string _card4SubValue = "синхронизация данных";
    
    // Card 1 segments
    [ObservableProperty] private int _card1Completed;
    [ObservableProperty] private int _card1InProgress;
    [ObservableProperty] private int _card1Planned;
    [ObservableProperty] private int _card1Total;
    
    // For gradient stops
    [ObservableProperty] private double _card1DoneOffset;
    [ObservableProperty] private double _card1InProgressOffset;

    // Card 3 segments (for visual breakdown)
    [ObservableProperty] private int _card3Segment1;
    [ObservableProperty] private int _card3Segment2;
    [ObservableProperty] private int _card3Segment3;
    [ObservableProperty] private int _card3Total;
    
    // For gradient stops
    [ObservableProperty] private double _card3Offset1;
    [ObservableProperty] private double _card3Offset2;
    
    [ObservableProperty] private string _lastSyncTime = "Только что";
    [ObservableProperty] private bool _isOnline = true;
    [ObservableProperty] private bool _isAdmin;
    
    // Detailed Tooltip Properties
    [ObservableProperty] private string _card1TooltipHeader = "Обзор нагрузки";
    [ObservableProperty] private string _card1TooltipDesc = "Текущее распределение объектов по статусам";
    [ObservableProperty] private string _card2TooltipHeader = "Ближайшее";
    [ObservableProperty] private string _card2TooltipDesc = "Самая приоритетная задача по срокам";
    [ObservableProperty] private int _card2Total;
    [ObservableProperty] private int _card2Segment1;
    [ObservableProperty] private int _card2Segment2;
    [ObservableProperty] private int _card2Segment3;
    
    [ObservableProperty] private string _card3TooltipHeader = "Зона риска";
    [ObservableProperty] private string _card3TooltipDesc = "Объекты, требующие немедленного внимания";
    [ObservableProperty] private string _card3Segment1Label = "Критический уровень";
    [ObservableProperty] private string _card3Segment2Label = "Высокий приоритет";
    [ObservableProperty] private string _card3Segment3Label = "Прочие замечания";
    
    [ObservableProperty] private string _card4TooltipHeader = "Статус системы";
    [ObservableProperty] private string _card4TooltipDesc = "Техническое состояние и синхронизация";

    private string _originalTitle = string.Empty;
    private string _originalXaml = string.Empty;

    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
    public string WelcomeMessage => $"Добрый день, {_auth.UserName?.Split(' ').FirstOrDefault() ?? "пользователь"}!";
    public string CurrentDateText => DateTime.Now.ToString("dd MMMM yyyy, dddd");

    private DispatcherTimer? _clockTimer;

    public HomeViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _sync = sync;

        _sync.OnlineStatusChanged += OnOnlineStatusChanged;
        SetupClock();
        UpdateSystemStatus();
    }

    private void OnOnlineStatusChanged(object? sender, bool isOnline)
    {
        // Update UI properties when sync status changes
        App.Current.Dispatcher.Invoke(() => UpdateSystemStatus());
    }

    private void UpdateSystemStatus()
    {
        IsOnline = _sync.IsOnline;
        var syncTime = _sync.LastSyncTime?.ToString("HH:mm:ss") ?? "—";
        LastSyncTime = syncTime; // Update the tooltip property too
        Card4Value = IsOnline ? "В сети" : "Офлайн";
        Card4SubValue = $"Синхронизация: {syncTime}";
        
        Card4TooltipDesc = IsOnline 
            ? $"Система подключена к серверу. Последний обмен данными: {syncTime}" 
            : "Система работает в автономном режиме. Изменения будут синхронизированы при восстановлении связи.";
    }

    private void SetupClock()
    {
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) => CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();
    }

    [RelayCommand]
    private async Task CreateNoteAsync()
    {
        var note = new LocalNote
        {
            UserId = _auth.UserId ?? Guid.Empty,
            Title = "",
            Content = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        SelectedNote = note;
        CurrentNoteXaml = "";
        _originalTitle = "";
        _originalXaml = "";
        IsNoteEditing = true;
    }

    [RelayCommand]
    private void SelectNote(LocalNote note)
    {
        SelectedNote = note;
        CurrentNoteXaml = note.Content;
        _originalTitle = note.Title ?? "";
        _originalXaml = note.Content ?? "";
        IsNoteEditing = true;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedNote == null) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            
            SelectedNote.Content = CurrentNoteXaml;
            SelectedNote.UpdatedAt = DateTime.UtcNow;
            SelectedNote.UserId = _auth.UserId ?? Guid.Empty;

            if (SelectedNote.Id == Guid.Empty || !await db.Notes.AnyAsync(n => n.Id == SelectedNote.Id))
            {
                if (SelectedNote.Id == Guid.Empty) SelectedNote.Id = Guid.NewGuid();
                db.Notes.Add(SelectedNote);
            }
            else
            {
                db.Notes.Update(SelectedNote);
            }

            await db.SaveChangesAsync();
            
            // Update original values to reflect the saved state
            _originalTitle = SelectedNote.Title ?? "";
            _originalXaml = SelectedNote.Content ?? "";
            
            await LoadNotesAsync();
        }
        catch (Exception ex)
        {
            SetError("Ошибка при сохранении заметки: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(LocalNote note)
    {
        if (note == null) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Notes.Remove(note);
            await db.SaveChangesAsync();
            
            if (SelectedNote?.Id == note.Id)
            {
                IsNoteEditing = false;
                SelectedNote = null;
            }
            
            await LoadNotesAsync();
        }
        catch (Exception ex)
        {
            SetError("Ошибка при удалении заметки: " + ex.Message);
        }
    }

    [RelayCommand]
    private void BackToList()
    {
        if (HasChanges())
        {
            IsBackConfirmPopupOpen = true;
        }
        else
        {
            CloseEditing();
        }
    }

    [RelayCommand]
    private async Task SaveNoteAndBackAsync()
    {
        await SaveNoteAsync();
        CloseEditing();
    }

    [RelayCommand]
    private void DiscardAndBack()
    {
        CloseEditing();
    }

    private bool HasChanges()
    {
        if (SelectedNote == null) return false;
        // Compare with original values
        return (SelectedNote.Title ?? "") != _originalTitle || (CurrentNoteXaml ?? "") != _originalXaml;
    }

    private void CloseEditing()
    {
        IsNoteEditing = false;
        SelectedNote = null;
        IsBackConfirmPopupOpen = false;
    }

    private async Task LoadNotesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var userId = _auth.UserId;
        
        var notesList = await db.Notes
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

        Notes = new ObservableCollection<LocalNote>(notesList);
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ClearMessages();

        try
        {
            // Update technical status immediately before heavy DB queries
            Card4Title = "Статус системы";
            UpdateSystemStatus();

            await using var db = await _dbFactory.CreateDbContextAsync();
            var userId = _auth.UserId ?? Guid.Empty;



            // 4. Recent Activities
            var activities = await ActivityFilterService.GetFilteredActivitiesAsync(db, _auth, 100, excludeAuthEvents: true);
            RecentActivities = new ObservableCollection<LocalActivityLog>(activities);

            // 5. Notes
            await LoadNotesAsync();

            // 6. Statistics (General)
            TotalFilesCount = await db.Files.CountAsync();
            
            var today = DateOnly.FromDateTime(DateTime.Today);
            var role = _auth.UserRole;

            // Normalize role for comparison
            IsAdmin = role is "Admin" or "Administrator" or "Админ";
            bool isManager = role is "Project Manager" or "ProjectManager" or "Менеджер" or "Менеджер проектов";
            bool isForeman = role is "Foreman" or "Прораб";
            bool isWorker = role is "Worker" or "Работник";

            // Helper for Russian pluralization
            string GetPlural(int n, string one, string two, string five)
            {
                int n1 = Math.Abs(n) % 100;
                int n2 = n1 % 10;
                if (n1 > 10 && n1 < 20) return $"{n} {five}";
                if (n2 > 1 && n2 < 5) return $"{n} {two}";
                if (n2 == 1) return $"{n} {one}";
                return $"{n} {five}";
            }

            // ── CARD 1: STATUS LINE (Load Overview) ──
            if (isWorker || isForeman)
            {
                Card1Title = "Обзор нагрузки";
                var workerStageIds = await db.StageAssignees.Where(sa => sa.UserId == userId).Select(sa => sa.StageId).ToListAsync();
                var workerTaskIds = await db.TaskAssignees.Where(ta => ta.UserId == userId).Select(ta => ta.TaskId).ToListAsync();
                var foremanProjectIds = isForeman ? await db.ProjectMembers.Where(m => m.UserId == userId).Select(m => m.ProjectId).ToListAsync() : new List<Guid>();

                // All stages assigned to user (directly or via parent task/project)
                var stagesQuery = from s in db.TaskStages
                                 join t in db.Tasks on s.TaskId equals t.Id
                                 join p in db.Projects on t.ProjectId equals p.Id
                                 where (s.AssignedUserId == userId || workerStageIds.Contains(s.Id) || 
                                        t.AssignedUserId == userId || workerTaskIds.Contains(t.Id) ||
                                        (isForeman && foremanProjectIds.Contains(p.Id)))
                                    && !s.IsMarkedForDeletion && !s.IsArchived
                                    && !t.IsMarkedForDeletion && !t.IsArchived
                                    && !p.IsMarkedForDeletion && !p.IsArchived
                                 select s;

                // Tasks assigned to user (if they have no stages, they are distinct work items)
                var tasksQuery = from t in db.Tasks
                                 join p in db.Projects on t.ProjectId equals p.Id
                                 where (t.AssignedUserId == userId || workerTaskIds.Contains(t.Id) ||
                                        (isForeman && foremanProjectIds.Contains(p.Id)))
                                    && !db.TaskStages.Any(s => s.TaskId == t.Id) // Only tasks WITHOUT stages (others are counted via stages)
                                    && !t.IsMarkedForDeletion && !t.IsArchived
                                    && !p.IsMarkedForDeletion && !p.IsArchived
                                 select t;

                int stagesCompleted = await stagesQuery.CountAsync(s => s.Status == StageStatus.Completed);
                int stagesInProgress = await stagesQuery.CountAsync(s => s.Status == StageStatus.InProgress);
                int stagesPlanned = await stagesQuery.CountAsync(s => s.Status == StageStatus.Planned);

                int tasksCompleted = await tasksQuery.CountAsync(t => t.Status == Models.TaskStatus.Completed);
                int tasksInProgress = await tasksQuery.CountAsync(t => t.Status == Models.TaskStatus.InProgress || t.Status == Models.TaskStatus.Paused);
                int tasksPlanned = await tasksQuery.CountAsync(t => t.Status == Models.TaskStatus.Planned);

                Card1Completed = stagesCompleted + tasksCompleted;
                Card1InProgress = stagesInProgress + tasksInProgress;
                Card1Planned = stagesPlanned + tasksPlanned;
                Card1Total = Card1Completed + Card1InProgress + Card1Planned;
                
                // Calculate offsets for gradient (0.0 to 1.0)
                if (Card1Total > 0)
                {
                    Card1DoneOffset = (double)Card1Completed / Card1Total;
                    Card1InProgressOffset = (double)(Card1Completed + Card1InProgress) / Card1Total;
                }
                else
                {
                    Card1DoneOffset = 0;
                    Card1InProgressOffset = 0;
                }
                
                // Terminology: for workers/foremen we show tasks/stages
                Card1Value = GetPlural(Card1Total, "задача", "задачи", "задач");
                Card1SubValue = isWorker ? "ваша нагрузка" : "задачи и этапы проектов";
                Card1TooltipDesc = isWorker 
                    ? "Ваш текущий план работ (задачи и этапы):" 
                    : "Общая нагрузка по всем вашим проектам:";

                // Card 2: Next Action
                var nextStage = await stagesQuery.Where(s => s.Status != StageStatus.Completed)
                    .OrderBy(s => s.DueDate == null).ThenBy(s => s.DueDate).FirstOrDefaultAsync();
                var nextTask = await tasksQuery.Where(t => t.Status != Models.TaskStatus.Completed)
                    .OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).FirstOrDefaultAsync();

                Card2Title = "Ближайшее";
                if (nextStage != null && (nextTask == null || (nextStage.DueDate ?? DateOnly.MaxValue) <= (nextTask.DueDate ?? DateOnly.MaxValue)))
                {
                    Card2Value = nextStage.Name;
                    Card2SubValue = nextStage.DueDate?.ToString("dd.MM.yyyy") ?? "Срок не задан";
                    
                    if (nextStage.DueDate.HasValue)
                    {
                        var days = (nextStage.DueDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days;
                        Card2TooltipDesc = days switch {
                            0 => "Крайний срок — СЕГОДНЯ. Нужно завершить как можно скорее.",
                            1 => "Крайний срок — завтра. Пора приступать к финализации.",
                            > 1 => $"До дедлайна осталось {GetPlural(days, "день", "дня", "дней")}.",
                            _ => "Срок выполнения уже ИСТЕК. Требуется срочное завершение."
                        };
                    }
                    else
                    {
                        Card2TooltipDesc = "Срок выполнения не установлен.";
                    }
                }
                else if (nextTask != null)
                {
                    Card2Value = nextTask.Name;
                    Card2SubValue = nextTask.DueDate?.ToString("dd.MM.yyyy") ?? "Срок не задан";

                    if (nextTask.DueDate.HasValue)
                    {
                        var days = (nextTask.DueDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days;
                        Card2TooltipDesc = days switch {
                            0 => "Крайний срок — СЕГОДНЯ. Не забудьте отметить выполнение.",
                            1 => "Крайний срок — завтра. Рекомендуется проверить готовность.",
                            > 1 => $"До дедлайна осталось {GetPlural(days, "день", "дня", "дней")}.",
                            _ => "Срок выполнения уже ИСТЕК. Задача находится в просрочке."
                        };
                    }
                    else
                    {
                        Card2TooltipDesc = "Срок выполнения не установлен.";
                    }
                }
                else
                {
                    Card2Value = isWorker ? "Нет этапов" : "Нет задач";
                    Card2SubValue = "—";
                    Card2TooltipDesc = "На данный момент у вас нет активных задач с установленным сроком.";
                }

                // For Attention, we count ALL overdue tasks assigned to user
                var overdueTasksCount = await (from t in db.Tasks
                                             join p in db.Projects on t.ProjectId equals p.Id
                                             where (t.AssignedUserId == userId || workerTaskIds.Contains(t.Id) ||
                                                    (isForeman && foremanProjectIds.Contains(p.Id)))
                                                && t.Status != Models.TaskStatus.Completed 
                                                && t.DueDate < today
                                                && !t.IsArchived
                                                && !p.IsArchived
                                             select t.Id).CountAsync();

                // For Attention, we count ALL overdue stages assigned to user
                var overdueStagesCount = await (from s in db.TaskStages
                                               join t in db.Tasks on s.TaskId equals t.Id
                                               join p in db.Projects on t.ProjectId equals p.Id
                                               where (s.AssignedUserId == userId || workerStageIds.Contains(s.Id) || 
                                                      t.AssignedUserId == userId || workerTaskIds.Contains(t.Id) ||
                                                      (isForeman && foremanProjectIds.Contains(p.Id)))
                                                  && s.Status != StageStatus.Completed 
                                                  && s.DueDate < today
                                                  && !s.IsArchived
                                                  && !t.IsArchived
                                                  && !p.IsArchived
                                               select s.Id).CountAsync();
                
                Card3Value = overdueStagesCount + overdueTasksCount;
                Card3Segment1 = overdueTasksCount;
                Card3Segment2 = overdueStagesCount;
                Card3Segment3 = 0;
                Card3Total = Card3Value;

                Card3Segment1Label = "Просроченные задачи:";
                Card3Segment2Label = "Просроченные этапы:";
                Card3Segment3Label = "";
                
                if (Card3Total > 0)
                {
                    Card3Offset1 = (double)Card3Segment1 / Card3Total;
                    Card3Offset2 = (double)(Card3Segment1 + Card3Segment2) / Card3Total;
                }
                else
                {
                    Card3Offset1 = 0;
                    Card3Offset2 = 0;
                }
                
                if (isWorker)
                {
                    if (Card3Value == 0)
                    {
                        Card3SubValue = "Все по графику";
                        Card3TooltipDesc = "Отлично! У вас нет просроченных задач или этапов.";
                    }
                    else
                    {
                        Card3SubValue = $"{overdueTasksCount} зад., {overdueStagesCount} эт. просрочено";
                        Card3TooltipDesc = "Внимание! Обнаружены объекты с истекшим сроком выполнения:";
                    }
                }
                else // Foreman
                {
                    // For foreman, also count unassigned tasks in their projects
                    int unassigned = await db.Tasks.CountAsync(t => foremanProjectIds.Contains(t.ProjectId) && 
                                                                   t.AssignedUserId == null && 
                                                                   !t.IsMarkedForDeletion && !t.IsArchived);
                    Card3Value = overdueStagesCount + overdueTasksCount + unassigned;
                    Card3Segment1 = overdueTasksCount;
                    Card3Segment2 = overdueStagesCount;
                    Card3Segment3 = unassigned;
                    Card3Total = Card3Value;

                    Card3Segment1Label = "Просроченные задачи:";
                    Card3Segment2Label = "Просроченные этапы:";
                    Card3Segment3Label = "Без ответственного:";
                    
                    if (Card3Total > 0)
                    {
                        Card3Offset1 = (double)Card3Segment1 / Card3Total;
                        Card3Offset2 = (double)(Card3Segment1 + Card3Segment2) / Card3Total;
                    }
                    else
                    {
                        Card3Offset1 = 0;
                        Card3Offset2 = 0;
                    }
                    
                    if (Card3Value == 0)
                    {
                        Card3SubValue = "Проблем не обнаружено";
                        Card3TooltipDesc = "Все задачи в ваших проектах распределены и выполняются в срок.";
                    }
                    else
                    {
                        Card3SubValue = $"{overdueTasksCount + overdueStagesCount} просроч., {unassigned} без отв.";
                        Card3TooltipDesc = "Требуется вмешательство прораба для следующих проблем:";
                    }
                }
            }
            else if (isManager || IsAdmin)
            {
                Card1Title = "Обзор нагрузки";
                var query = db.Projects.Where(p => !p.IsMarkedForDeletion && !p.IsArchived);
                if (isManager) query = query.Where(p => p.ManagerId == userId);

                var projects = await query.ToListAsync();

                var projectIds = projects.Select(p => p.Id).ToList();
                var allTasks = await db.Tasks.Where(t => projectIds.Contains(t.ProjectId)).ToListAsync();
                var allStages = await (from s in db.TaskStages
                                       join t in db.Tasks on s.TaskId equals t.Id
                                       where projectIds.Contains(t.ProjectId)
                                       select s).ToListAsync();

                foreach (var p in projects)
                {
                    var pTasks = allTasks.Where(t => t.ProjectId == p.Id && !t.IsMarkedForDeletion && !t.IsArchived).ToList();
                    var pTaskIds = pTasks.Select(t => t.Id).ToHashSet();
                    var pStages = allStages.Where(s => pTaskIds.Contains(s.TaskId) && !s.IsArchived).ToList();
                    
                    foreach (var t in pTasks)
                    {
                        var tStages = pStages.Where(s => s.TaskId == t.Id).ToList();
                        ProgressCalculator.ApplyTaskMetrics(t, tStages);
                    }

                    ProgressCalculator.ApplyProjectMetrics(p, pTasks, pStages);
                }

                Card1Completed = projects.Count(p => p.Status == ProjectStatus.Completed);
                Card1InProgress = projects.Count(p => p.Status == ProjectStatus.InProgress);
                Card1Planned = projects.Count(p => p.Status == ProjectStatus.Planning);
                Card1Total = Card1Completed + Card1InProgress + Card1Planned;

                if (Card1Total > 0)
                {
                    Card1DoneOffset = (double)Card1Completed / Card1Total;
                    Card1InProgressOffset = (double)(Card1Completed + Card1InProgress) / Card1Total;
                }
                else
                {
                    Card1DoneOffset = 0;
                    Card1InProgressOffset = 0;
                }

                Card1Value = GetPlural(Card1Total, "проект", "проекта", "проектов");
                Card1SubValue = isManager ? "ваши активные проекты" : "все проекты в системе";
                Card1TooltipDesc = isManager 
                    ? "Статистика по курируемым вами проектам:" 
                    : "Общий обзор всех проектов в организации:";

                // Card 2: Next Action or Users
                if (isManager)
                {
                    var nextProject = await query.Where(p => p.Status != ProjectStatus.Completed)
                        .OrderBy(p => p.EndDate == null).ThenBy(p => p.EndDate).FirstOrDefaultAsync();
                    Card2Title = "Ближайшее";
                    Card2Value = nextProject?.Name ?? "Нет дедлайнов";
                    Card2SubValue = nextProject?.EndDate?.ToString("dd.MM.yyyy") ?? "—";

                    if (nextProject != null)
                    {
                        if (nextProject.EndDate.HasValue)
                        {
                            var days = (nextProject.EndDate.Value.ToDateTime(TimeOnly.MinValue) - DateTime.Today).Days;
                            Card2TooltipDesc = days switch {
                                0 => "Проект должен быть завершен СЕГОДНЯ. Проверьте финальные этапы.",
                                1 => "Дедлайн проекта завтра. Убедитесь, что все задачи закрыты.",
                                > 1 => $"До завершения проекта осталось {GetPlural(days, "день", "дня", "дней")}.",
                                _ => "Проект ПРОСРОЧЕН по срокам. Требуется отчет о причинах задержки."
                            };
                        }
                        else
                        {
                            Card2TooltipDesc = "Для проекта не установлен срок завершения.";
                        }
                    }
                    else
                    {
                        Card2TooltipDesc = "У ваших текущих проектов не установлены крайние сроки.";
                    }
                }
                else // Admin
                {
                    Card2Title = "На удаление";
                    var projectsMarked = await db.Projects.CountAsync(p => p.IsMarkedForDeletion);
                    var tasksMarked = await db.Tasks.CountAsync(t => t.IsMarkedForDeletion);
                    var stagesMarked = await db.TaskStages.CountAsync(s => s.IsMarkedForDeletion);
                    var totalMarked = projectsMarked + tasksMarked + stagesMarked;

                    Card2Value = GetPlural(totalMarked, "объект", "объекта", "объектов");
                    Card2SubValue = "ожидают подтверждения";
                    
                    Card2Segment1 = projectsMarked;
                    Card2Segment2 = tasksMarked;
                    Card2Segment3 = stagesMarked;
                    Card2Total = totalMarked;

                    Card2TooltipHeader = "Объекты на удаление";
                    Card2TooltipDesc = "Список объектов, ожидающих подтверждения администратора для окончательного удаления.";
                }

                // Card 3: Attention (Risk Zone)
                Card3Title = "Внимание";
                if (isManager)
                {
                    var managerProjectIds = await query.Select(p => p.Id).ToListAsync();
                    int overdueProj = await query.CountAsync(p => p.Status != ProjectStatus.Completed && p.EndDate < today);
                    int overdueTasksInProj = await db.Tasks.CountAsync(t => managerProjectIds.Contains(t.ProjectId) && 
                                                                          t.Status != Models.TaskStatus.Completed && 
                                                                          t.DueDate < today && 
                                                                          !t.IsArchived);
                    
                    int overdueStagesInProj = await (from s in db.TaskStages
                                                     join t in db.Tasks on s.TaskId equals t.Id
                                                     where managerProjectIds.Contains(t.ProjectId)
                                                        && s.Status != StageStatus.Completed
                                                        && s.DueDate < today
                                                        && !s.IsArchived
                                                        && !t.IsArchived
                                                     select s).CountAsync();
                                                     
                    Card3Value = overdueProj + overdueTasksInProj + overdueStagesInProj;
                    Card3Segment1 = overdueProj;
                    Card3Segment2 = overdueTasksInProj;
                    Card3Segment3 = overdueStagesInProj;
                    Card3Total = Card3Value;

                    Card3Segment1Label = "Просроченные проекты:";
                    Card3Segment2Label = "Просроченные задачи:";
                    Card3Segment3Label = "Просроченные этапы:";
                    
                    if (Card3Total > 0)
                    {
                        Card3Offset1 = (double)Card3Segment1 / Card3Total;
                        Card3Offset2 = (double)(Card3Segment1 + Card3Segment2) / Card3Total;
                    }
                    else
                    {
                        Card3Offset1 = 0;
                        Card3Offset2 = 0;
                    }
                    
                    if (Card3Value == 0)
                    {
                        Card3SubValue = "Задержек нет";
                        Card3TooltipDesc = "Все проекты и задачи в вашей зоне ответственности идут по плану.";
                    }
                    else
                    {
                        Card3SubValue = $"{overdueProj} пр., {overdueTasksInProj} зад., {overdueStagesInProj} эт. в риске";
                        Card3TooltipDesc = "Обнаружены критические задержки в курируемых проектах:";
                    }
                }
                else // Admin
                {
                    // Global overdue counts for admin (only in active projects/tasks)
                    int globalOverdueProjects = await db.Projects.CountAsync(p => !p.IsMarkedForDeletion && !p.IsArchived && p.Status != ProjectStatus.Completed && p.EndDate < today);

                    int globalOverdueTasks = await (from t in db.Tasks
                                                   join p in db.Projects on t.ProjectId equals p.Id
                                                   where t.Status != Models.TaskStatus.Completed
                                                      && t.DueDate < today
                                                      && !t.IsArchived
                                                      && !p.IsArchived
                                                   select t.Id).CountAsync();
                                                   
                    int globalOverdueStages = await (from s in db.TaskStages
                                                    join t in db.Tasks on s.TaskId equals t.Id
                                                    join p in db.Projects on t.ProjectId equals p.Id
                                                    where s.Status != StageStatus.Completed
                                                       && s.DueDate < today
                                                       && !s.IsArchived
                                                       && !t.IsArchived
                                                       && !p.IsArchived
                                                    select s.Id).CountAsync();
                    
                    Card3Value = globalOverdueProjects + globalOverdueTasks + globalOverdueStages;
                    Card3Segment1 = globalOverdueProjects;
                    Card3Segment2 = globalOverdueTasks;
                    Card3Segment3 = globalOverdueStages;
                    Card3Total = Card3Value;

                    Card3Segment1Label = "Просроченные проекты:";
                    Card3Segment2Label = "Просроченные задачи:";
                    Card3Segment3Label = "Просроченные этапы:";
                    
                    if (Card3Total > 0)
                    {
                        Card3Offset1 = (double)Card3Segment1 / Card3Total;
                        Card3Offset2 = (double)(Card3Segment1 + Card3Segment2) / Card3Total;
                    }
                    else
                    {
                        Card3Offset1 = 0;
                        Card3Offset2 = 0;
                    }
                    
                    if (Card3Value == 0)
                    {
                        Card3SubValue = "Проблем не обнаружено";
                        Card3TooltipDesc = "Все проекты в системе выполняются согласно графику.";
                    }
                    else
                    {
                        Card3SubValue = $"{globalOverdueProjects} пр., {globalOverdueTasks} зад., {globalOverdueStages} эт. в риске";
                        Card3TooltipDesc = "Обнаружены глобальные задержки в проектах организации:";
                    }
                }
            }

            // Technical status is now updated at the beginning of LoadAsync

        }
        catch (Exception ex)
        {
            SetError("Ошибка при загрузке данных: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
