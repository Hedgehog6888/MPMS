using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

// ─────────────────────────────────────────────────────────────────────────
// Display model for user rows in the admin panel
// ─────────────────────────────────────────────────────────────────────────
public class AdminUserRow : ObservableObject
{
    public Guid   Id             { get; set; }
    public string Name           { get; set; } = string.Empty;
    public string FirstName      { get; set; } = string.Empty;
    public string LastName       { get; set; } = string.Empty;
    public string Username       { get; set; } = string.Empty;
    public string Email          { get; set; } = string.Empty;
    public DateOnly? BirthDate   { get; set; }
    public string? HomeAddress   { get; set; }
    public string RoleName       { get; set; } = string.Empty;
    public string RoleDisplay    { get; set; } = string.Empty;
    public Guid   RoleId         { get; set; }
    public string? SubRole            { get; set; }
    public string? AdditionalSubRoles { get; set; }
    public DateTime CreatedAt    { get; set; }
    public bool   IsBlocked      { get; set; }
    public string? BlockedReason { get; set; }
    public DateTime? BlockedAt   { get; set; }
    public byte[]? AvatarData    { get; set; }
    public string? AvatarPath    { get; set; }

    public string Initials     => AvatarHelper.GetInitials(Name);
    public string AvatarColor  => AvatarHelper.GetColorForName(Name);
    public string StatusText   => IsBlocked ? "Заблокирован" : "Активен";
    public string BlockIcon    => IsBlocked ? "🔓" : "🔒";
    public string BlockLabel   => IsBlocked ? "Разблокировать" : "Заблокировать";

    public string RoleColor => RoleName switch
    {
        "Administrator" or "Admin"                              => "#FEE2E2",
        "Project Manager" or "ProjectManager" or "Manager"     => "#DBEAFE",
        "Foreman"                                               => "#D1FAE5",
        "Worker"                                                => "#EDE9FE",
        _                                                       => "#F1F3F5"
    };
    public string RoleForeground => RoleName switch
    {
        "Administrator" or "Admin"                              => "#991B1B",
        "Project Manager" or "ProjectManager" or "Manager"     => "#1D4ED8",
        "Foreman"                                               => "#166534",
        "Worker"                                                => "#6D28D9",
        _                                                       => "#4B5563"
    };
}

// ─────────────────────────────────────────────────────────────────────────
// Display model for archived entity rows
// ─────────────────────────────────────────────────────────────────────────
public class ArchiveRow
{
    public Guid   Id          { get; set; }
    public string EntityType  { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
    public string ParentName  { get; set; } = string.Empty;
    public string StatusText  { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; }
    public string DeletedBy   { get; set; } = string.Empty;
    public string? Description { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────
// Role item for the form combo
// ─────────────────────────────────────────────────────────────────────────
public class RoleItem
{
    public Guid   Id      { get; set; }
    public string Name    { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public override string ToString() => Display;
}

// ─────────────────────────────────────────────────────────────────────────
// Main AdminViewModel
// ─────────────────────────────────────────────────────────────────────────
public partial class AdminViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;
    private readonly IApiService _api;
    private readonly ISyncService _sync;

    // Events to open drawers (handled by AdminPage.xaml.cs)
    public event Action<AdminUserRow>? OpenUserInfoRequested;
    public event Action? OpenCreateFormRequested;
    public event Action<AdminUserRow>? OpenEditFormRequested;

    // Static filter options
    public static readonly IReadOnlyList<string> RoleFilterOptions   = ["Все", "Администратор", "Менеджер", "Прораб", "Работник"];
    public static readonly IReadOnlyList<string> StatusFilterOptions = ["Все", "Активные", "Заблокированные"];

    // History action type options (display name → action kind constant)
    public static readonly IReadOnlyList<string> HistoryActionOptions =
        ["Все", "Создано", "Изменено", "Удалено/Архив", "Восстановлено", "Сообщение"];
    private static readonly Dictionary<string, string[]> HistoryActionMap = new()
    {
        ["Создано"]       = [ActivityActionKind.Created],
        ["Изменено"]      = [ActivityActionKind.Updated, ActivityActionKind.UserEdited],
        ["Удалено/Архив"] = [ActivityActionKind.Deleted, ActivityActionKind.MarkedForDeletion, ActivityActionKind.UserDeleted, ActivityActionKind.PermanentlyDeleted],
        ["Восстановлено"] = [ActivityActionKind.Restored, ActivityActionKind.UnmarkedForDeletion, ActivityActionKind.UserUnblocked],
        ["Сообщение"]     = [ActivityActionKind.Message],
    };

    // Activity event type options
    public static readonly IReadOnlyList<string> ActivityEventOptions =
        ["Все", "Вход", "Выход", "Смена пароля", "Смена аватара", "Заблокирован"];
    private static readonly Dictionary<string, string[]> ActivityEventMap = new()
    {
        ["Вход"]          = [ActivityActionKind.Login],
        ["Выход"]         = [ActivityActionKind.Logout],
        ["Смена пароля"]  = [ActivityActionKind.PasswordChanged],
        ["Смена аватара"] = [ActivityActionKind.AvatarChanged],
        ["Заблокирован"]  = [ActivityActionKind.UserBlocked, ActivityActionKind.UserUnblocked],
    };

    private const int PageSize = 50;

    // ── Tab navigation ────────────────────────────────────────────────────
    [ObservableProperty] private string _currentTab = "Users";

    // ── Users tab ─────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<AdminUserRow> _users = new();
    [ObservableProperty] private ObservableCollection<AdminUserRow> _filteredUsers = new();
    [ObservableProperty] private string _userSearchText   = string.Empty;
    [ObservableProperty] private string _userRoleFilter   = "Все";
    [ObservableProperty] private string _userStatusFilter = "Все";
    [ObservableProperty] private int _totalUsersCount;
    [ObservableProperty] private int _activeUsersCount;
    [ObservableProperty] private int _blockedUsersCount;

    // ── Archive tab ───────────────────────────────────────────────────────
    [ObservableProperty] private string _archiveTab = "Projects";
    [ObservableProperty] private ObservableCollection<ArchiveRow> _archivedProjects = new();
    [ObservableProperty] private ObservableCollection<ArchiveRow> _archivedTasks    = new();
    [ObservableProperty] private ObservableCollection<ArchiveRow> _archivedStages   = new();
    [ObservableProperty] private int _archiveProjectCount;
    [ObservableProperty] private int _archiveTaskCount;
    [ObservableProperty] private int _archiveStageCount;
    [ObservableProperty] private string _archiveSearchText = string.Empty;
    [ObservableProperty] private ObservableCollection<ArchiveRow> _filteredArchivedProjects = new();
    [ObservableProperty] private ObservableCollection<ArchiveRow> _filteredArchivedTasks    = new();
    [ObservableProperty] private ObservableCollection<ArchiveRow> _filteredArchivedStages   = new();

    // ── History tab ───────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _historyLogs = new();
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _filteredHistoryLogs = new();
    [ObservableProperty] private string _historySearchText    = string.Empty;
    [ObservableProperty] private string _historyActionFilter  = "Все";
    [ObservableProperty] private string _historyUserFilter    = "Все";
    [ObservableProperty] private ObservableCollection<string> _historyUserList = new();
    [ObservableProperty] private int  _historyTotalCount;
    [ObservableProperty] private bool _hasMoreHistory;
    private int _historyLoadedCount = PageSize;
    private List<LocalActivityLog> _allHistoryLogs = new();

    // ── Activity tab ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _activityLogs = new();
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _filteredActivityLogs = new();
    [ObservableProperty] private string _activitySearchText   = string.Empty;
    [ObservableProperty] private string _activityUserFilter   = "Все";
    [ObservableProperty] private string _activityEventFilter  = "Все";
    [ObservableProperty] private ObservableCollection<string> _activityUserList = new();
    [ObservableProperty] private int  _activityTotalCount;
    [ObservableProperty] private bool _hasMoreActivity;
    private int _activityLoadedCount = PageSize;
    private List<LocalActivityLog> _allActivityLogs = new();

    // ── Block overlay ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _isBlockOverlayOpen;
    [ObservableProperty] private bool _isUnblockOverlayOpen;
    [ObservableProperty] private string _blockTargetName = string.Empty;
    [ObservableProperty] private string _blockTargetReason = string.Empty; // reason when unblocking (read-only)
    [ObservableProperty] private string _blockReason     = string.Empty;
    private AdminUserRow? _blockTargetRow;

    // ── Confirm overlay ───────────────────────────────────────────────────
    [ObservableProperty] private bool   _isConfirmOverlayOpen;
    [ObservableProperty] private string _confirmTitle       = string.Empty;
    [ObservableProperty] private string _confirmEntityName  = string.Empty;
    [ObservableProperty] private string _confirmButtonText  = "Подтвердить";
    [ObservableProperty] private bool   _confirmIsDestructive;
    private Func<Task>? _confirmAction;

    private void SetupConfirm(string title, string entityName, string buttonText, bool destructive, Func<Task> action)
    {
        ConfirmTitle        = title;
        ConfirmEntityName   = entityName;
        ConfirmButtonText   = buttonText;
        ConfirmIsDestructive = destructive;
        _confirmAction      = action;
        IsConfirmOverlayOpen = true;
    }

    public AdminViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, IApiService api, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _auth      = auth;
        _api       = api;
        _sync      = sync;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await LoadUsersAsync();
            await LoadArchiveAsync();
            await LoadHistoryAsync();
            await LoadActivityAsync();
        }
        finally { IsBusy = false; }
    }

    /// <summary>Called by overlays after creating/editing a user.</summary>
    public async Task RefreshAfterUserChangeAsync() => await LoadUsersAsync();

    // ══════════════════════════════════════════════════════════════════════
    // USERS TAB — load + filter + actions
    // ══════════════════════════════════════════════════════════════════════

    public async Task LoadUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.Users.OrderBy(u => u.Name).ToListAsync();

        var rows = users.Select(u =>
            {
                var name = !string.IsNullOrWhiteSpace(u.Name) ? u.Name : $"{u.FirstName} {u.LastName}".Trim();
                var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                return new AdminUserRow
                {
                Id            = u.Id,
                Name          = name,
                FirstName     = parts.Length > 0 ? parts[0] : u.FirstName,
                LastName      = parts.Length > 1 ? parts[1] : u.LastName,
                Username      = u.Username,
                Email         = u.Email ?? string.Empty,
                BirthDate     = u.BirthDate,
                HomeAddress   = u.HomeAddress,
                RoleName      = u.RoleName,
                RoleDisplay   = u.RoleDisplayName,
                RoleId        = u.RoleId,
                SubRole            = u.SubRole,
                AdditionalSubRoles = u.AdditionalSubRoles,
                CreatedAt     = u.CreatedAt,
                IsBlocked     = u.IsBlocked,
                BlockedReason = u.BlockedReason,
                BlockedAt     = u.BlockedAt,
                AvatarData    = u.AvatarData,
                AvatarPath    = u.AvatarPath
                };
            }).ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            Users.Clear();
            foreach (var r in rows) Users.Add(r);
            TotalUsersCount   = rows.Count;
            ActiveUsersCount  = rows.Count(r => !r.IsBlocked);
            BlockedUsersCount = rows.Count(r => r.IsBlocked);
            ApplyUserFilter();
        });
    }

    partial void OnUserSearchTextChanged(string value)   => ApplyUserFilter();
    partial void OnUserRoleFilterChanged(string value)   => ApplyUserFilter();
    partial void OnUserStatusFilterChanged(string value) => ApplyUserFilter();

    private void ApplyUserFilter()
    {
        var query = Users.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(UserSearchText))
        {
            var s = UserSearchText.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.Name.ToLowerInvariant().Contains(s) ||
                u.Username.ToLowerInvariant().Contains(s) ||
                u.Email.ToLowerInvariant().Contains(s));
        }

        if (UserRoleFilter != "Все")
            query = query.Where(u => u.RoleDisplay == UserRoleFilter);

        if (UserStatusFilter == "Активные")
            query = query.Where(u => !u.IsBlocked);
        else if (UserStatusFilter == "Заблокированные")
            query = query.Where(u => u.IsBlocked);

        FilteredUsers.Clear();
        foreach (var u in query) FilteredUsers.Add(u);
    }

    // ── Commands to open drawers ──────────────────────────────────────────

    [RelayCommand]
    private void OpenCreateForm() => OpenCreateFormRequested?.Invoke();

    [RelayCommand]
    private void OpenEditForm(AdminUserRow? row)
    {
        if (row is not null) OpenEditFormRequested?.Invoke(row);
    }

    [RelayCommand]
    private void ViewUserInfo(AdminUserRow? row)
    {
        if (row is not null) OpenUserInfoRequested?.Invoke(row);
    }

    // ── Block/Unblock ─────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenBlockOverlay(AdminUserRow? row)
    {
        if (row is null) return;
        _blockTargetRow = row;
        BlockTargetName = row.Name;
        BlockTargetReason = row.BlockedReason ?? string.Empty;
        BlockReason = string.Empty;
        if (row.IsBlocked)
        {
            IsUnblockOverlayOpen = true;
            IsBlockOverlayOpen = false;
        }
        else
        {
            IsBlockOverlayOpen = true;
            IsUnblockOverlayOpen = false;
        }
    }

    [RelayCommand]
    private void CancelBlockOverlay() => IsBlockOverlayOpen = false;

    [RelayCommand]
    private void CancelUnblockOverlay() => IsUnblockOverlayOpen = false;

    [RelayCommand]
    private async Task SubmitBlockAsync()
    {
        if (_blockTargetRow is null) return;
        var row = _blockTargetRow;
        IsBlockOverlayOpen = false;
        IsUnblockOverlayOpen = false;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(row.Id);
        if (user is null) return;

        bool newBlocked = !user.IsBlocked;
        user.IsBlocked     = newBlocked;
        user.BlockedAt     = newBlocked ? DateTime.UtcNow : null;
        user.BlockedReason = newBlocked ? BlockReason.Trim() : null;
        user.LastModifiedLocally = DateTime.UtcNow;

        var userName = !string.IsNullOrWhiteSpace(user.Name) ? user.Name : $"{user.FirstName} {user.LastName}".Trim();
        var label = newBlocked ? "Заблокированный" : userName;
        foreach (var m in await db.ProjectMembers.Where(x => x.UserId == row.Id).ToListAsync()) m.UserName = label;
        foreach (var t in await db.TaskAssignees.Where(x => x.UserId == row.Id).ToListAsync()) t.UserName = label;
        foreach (var s in await db.StageAssignees.Where(x => x.UserId == row.Id).ToListAsync()) s.UserName = label;
        foreach (var m in await db.Messages.Where(x => x.UserId == row.Id).ToListAsync()) m.UserName = label;

        AddAdminLog(db,
            newBlocked ? ActivityActionKind.UserBlocked : ActivityActionKind.UserUnblocked,
            newBlocked
                ? $"Заблокировал пользователя {user.Name} ({user.Username}). Причина: {BlockReason}"
                : $"Разблокировал пользователя {user.Name} ({user.Username})",
            "User", user.Id);
        await db.SaveChangesAsync();
        var updateReq = new UpdateUserRequest(
            user.FirstName, user.LastName, user.Username, user.Email, user.RoleId,
            NewPassword: null,
            user.SubRole, user.AdditionalSubRoles, user.BirthDate, user.HomeAddress,
            IsBlocked: newBlocked,
            BlockedReason: newBlocked ? BlockReason.Trim() : null);
        await _sync.QueueOperationAsync("UserProfile", user.Id, SyncOperation.Update, updateReq);
        await LoadUsersAsync();
        SetStatus(newBlocked ? $"Пользователь {user.Name} заблокирован" : $"Пользователь {user.Name} разблокирован");
    }

    // ── Delete user ───────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenDeleteUserConfirm(AdminUserRow? row)
    {
        if (row is null) return;
        if (row.Id == _auth.UserId) { SetStatus("Нельзя удалить текущего пользователя"); return; }

        var userId = row.Id;
        SetupConfirm(
            "Удалить пользователя?",
            row.Name,
            "Удалить",
            destructive: true,
            async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var user = await db.Users.FindAsync(userId);
                if (user is null) return;
                try
                {
                    // Удалить в API (если онлайн), чтобы синхронизация не вернула пользователя
                    var apiDeleted = !_api.IsOnline || await _api.DeleteUserAsync(userId);

                    // Запомнить ID — синхронизация не должна вернуть пользователя
                    if (!await db.DeletedUserIds.AnyAsync(x => x.Id == userId))
                        db.DeletedUserIds.Add(new DeletedUserId { Id = userId });

                    // Обновить имя в связанных записях (оставляем их, вместо имени — «Удалённый пользователь»)
                    const string deletedLabel = "Удалённый пользователь";
                    foreach (var m in await db.ProjectMembers.Where(x => x.UserId == userId).ToListAsync())
                        m.UserName = deletedLabel;
                    foreach (var t in await db.TaskAssignees.Where(x => x.UserId == userId).ToListAsync())
                        t.UserName = deletedLabel;
                    foreach (var s in await db.StageAssignees.Where(x => x.UserId == userId).ToListAsync())
                        s.UserName = deletedLabel;
                    foreach (var m in await db.Messages.Where(x => x.UserId == userId).ToListAsync())
                        m.UserName = deletedLabel;

                    AddAdminLog(db, ActivityActionKind.UserDeleted,
                        $"Удалил пользователя {user.Name} ({user.Username})", "User", user.Id);

                    // Удалить напрямую через SQL (обходит возможные проблемы с трекером EF)
                    await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
                    await db.SaveChangesAsync();
                    await LoadUsersAsync();
                    SetStatus(apiDeleted ? $"Пользователь {user.Name} удалён" : $"Пользователь {user.Name} удалён локально (на сервере — возможно, руководитель проекта)");
                }
                catch (Exception ex)
                {
                    SetStatus($"Ошибка удаления: {ex.Message}");
                }
            });
    }

    // ── Confirm overlay ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task ExecuteConfirmAsync()
    {
        IsConfirmOverlayOpen = false;
        if (_confirmAction is not null) await _confirmAction();
        _confirmAction = null;
    }

    [RelayCommand]
    private void CancelConfirm()
    {
        IsConfirmOverlayOpen = false;
        _confirmAction = null;
    }

    // ══════════════════════════════════════════════════════════════════════
    // ARCHIVE TAB
    // ══════════════════════════════════════════════════════════════════════

    private async Task LoadArchiveAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var projects = await db.Projects.Where(p => p.IsArchived).OrderByDescending(p => p.UpdatedAt).ToListAsync();

        // Only individually archived tasks (skip those cascade-archived with their project)
        var archivedProjectIds = projects.Select(p => p.Id).ToList();
        var tasks = await db.Tasks
            .Where(t => t.IsArchived && !archivedProjectIds.Contains(t.ProjectId))
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync();

        // Only individually archived stages (skip those cascade-archived with their task)
        var archivedTaskIds = await db.Tasks.Where(t => t.IsArchived).Select(t => t.Id).ToListAsync();
        var stages = await db.TaskStages
            .Where(s => s.IsArchived && !archivedTaskIds.Contains(s.TaskId))
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

        var stageTaskIds  = stages.Select(s => s.TaskId).Distinct().ToList();
        var taskNamesById = await db.Tasks
            .Where(t => stageTaskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        Application.Current.Dispatcher.Invoke(() =>
        {
            ArchivedProjects.Clear();
            foreach (var p in projects)
                ArchivedProjects.Add(new ArchiveRow { Id = p.Id, EntityType = "Project", Name = p.Name, ParentName = p.Client ?? "—",
                    StatusText = p.Status switch { ProjectStatus.Planning => "Планирование", ProjectStatus.InProgress => "В работе", ProjectStatus.Completed => "Завершён", _ => "—" },
                    DeletedAt = p.UpdatedAt, DeletedBy = p.ManagerName,
                    Description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description });

            ArchivedTasks.Clear();
            foreach (var t in tasks)
                ArchivedTasks.Add(new ArchiveRow { Id = t.Id, EntityType = "Task", Name = t.Name, ParentName = t.ProjectName,
                    StatusText = t.Status switch { Models.TaskStatus.Planned => "Запланирована", Models.TaskStatus.InProgress => "Выполняется", Models.TaskStatus.Completed => "Завершена", _ => "—" },
                    DeletedAt = t.UpdatedAt, DeletedBy = t.AssignedUserName ?? "—",
                    Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description });

            ArchivedStages.Clear();
            foreach (var s in stages)
                ArchivedStages.Add(new ArchiveRow { Id = s.Id, EntityType = "Stage", Name = s.Name,
                    ParentName = taskNamesById.GetValueOrDefault(s.TaskId, "—"),
                    StatusText = s.Status switch { StageStatus.Planned => "Запланирован", StageStatus.InProgress => "Выполняется", StageStatus.Completed => "Завершён", _ => "—" },
                    DeletedAt = s.UpdatedAt, DeletedBy = s.AssignedUserName ?? "—" });

            ArchiveProjectCount = projects.Count;
            ArchiveTaskCount    = tasks.Count;
            ArchiveStageCount   = stages.Count;
            ApplyArchiveFilter();
        });
    }

    partial void OnArchiveSearchTextChanged(string value) => ApplyArchiveFilter();

    private void ApplyArchiveFilter()
    {
        var s = ArchiveSearchText.Trim().ToLowerInvariant();
        FilteredArchivedProjects.Clear();
        foreach (var r in ArchivedProjects.Where(p => string.IsNullOrEmpty(s) || p.Name.ToLowerInvariant().Contains(s) || p.ParentName.ToLowerInvariant().Contains(s))) FilteredArchivedProjects.Add(r);
        FilteredArchivedTasks.Clear();
        foreach (var r in ArchivedTasks.Where(t => string.IsNullOrEmpty(s) || t.Name.ToLowerInvariant().Contains(s) || t.ParentName.ToLowerInvariant().Contains(s))) FilteredArchivedTasks.Add(r);
        FilteredArchivedStages.Clear();
        foreach (var r in ArchivedStages.Where(st => string.IsNullOrEmpty(s) || st.Name.ToLowerInvariant().Contains(s) || st.ParentName.ToLowerInvariant().Contains(s))) FilteredArchivedStages.Add(r);
    }

    [RelayCommand]
    private async Task RestoreProjectAsync(ArchiveRow? row)
    {
        if (row is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var p = await db.Projects.FindAsync(row.Id); if (p is null) return;
        p.IsArchived = false; p.UpdatedAt = DateTime.UtcNow;
        var tasks = await db.Tasks.Where(t => t.ProjectId == p.Id && t.IsArchived).ToListAsync();
        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId) && s.IsArchived).ToListAsync();
        foreach (var t in tasks) { t.IsArchived = false; t.UpdatedAt = DateTime.UtcNow; }
        foreach (var s in stages)
        {
            s.IsArchived = false;
            s.UpdatedAt = DateTime.UtcNow;
            await TryReserveRestoredStageEquipmentAsync(db, s, p.Id);
        }
        AddAdminLog(db, ActivityActionKind.Restored, $"Восстановил проект «{p.Name}» из архива", "Project", p.Id);
        await db.SaveChangesAsync(); await LoadArchiveAsync(); SetStatus($"Проект «{p.Name}» восстановлен");
    }

    [RelayCommand]
    private void OpenPermanentDeleteProjectConfirm(ArchiveRow? row)
    {
        if (row is null) return;
        SetupConfirm("Удалить навсегда?", row.Name, "Удалить навсегда", destructive: true, async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var p = await db.Projects.FindAsync(row.Id); if (p is null) return;
            AddAdminLog(db, ActivityActionKind.PermanentlyDeleted, $"Удалил навсегда проект «{p.Name}»", "Project", p.Id);
            db.Projects.Remove(p); await db.SaveChangesAsync(); await LoadArchiveAsync();
            SetStatus($"Проект «{p.Name}» удалён навсегда");
        });
    }

    [RelayCommand]
    private void OpenRestoreProjectConfirm(ArchiveRow? row)
    {
        if (row is null) return;
        SetupConfirm("Восстановить проект?", row.Name, "Восстановить", destructive: false,
            () => RestoreProjectAsync(row));
    }

    [RelayCommand]
    private async Task RestoreTaskAsync(ArchiveRow? row)
    {
        if (row is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var t = await db.Tasks.FindAsync(row.Id); if (t is null) return;
        t.IsArchived = false; t.UpdatedAt = DateTime.UtcNow;
        var stages = await db.TaskStages.Where(s => s.TaskId == t.Id && s.IsArchived).ToListAsync();
        foreach (var s in stages)
        {
            s.IsArchived = false;
            s.UpdatedAt = DateTime.UtcNow;
            await TryReserveRestoredStageEquipmentAsync(db, s, t.ProjectId);
        }
        AddAdminLog(db, ActivityActionKind.Restored, $"Восстановил задачу «{t.Name}» из архива", "Task", t.Id);
        await db.SaveChangesAsync(); await LoadArchiveAsync(); SetStatus($"Задача «{t.Name}» восстановлена");
    }

    [RelayCommand]
    private void OpenPermanentDeleteTaskConfirm(ArchiveRow? row)
    {
        if (row is null) return;
        SetupConfirm("Удалить навсегда?", row.Name, "Удалить навсегда", destructive: true, async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.Tasks.FindAsync(row.Id); if (t is null) return;
            AddAdminLog(db, ActivityActionKind.PermanentlyDeleted, $"Удалил навсегда задачу «{t.Name}»", "Task", t.Id);
            db.Tasks.Remove(t); await db.SaveChangesAsync(); await LoadArchiveAsync();
            SetStatus($"Задача «{t.Name}» удалена навсегда");
        });
    }

    [RelayCommand]
    private void OpenRestoreTaskConfirm(ArchiveRow? row)
    {
        if (row is null) return;
        SetupConfirm("Восстановить задачу?", row.Name, "Восстановить", destructive: false,
            () => RestoreTaskAsync(row));
    }

    [RelayCommand]
    private async Task RestoreStageAsync(ArchiveRow? row)
    {
        if (row is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var s = await db.TaskStages.FindAsync(row.Id); if (s is null) return;
        s.IsArchived = false;
        s.UpdatedAt = DateTime.UtcNow;
        var task = await db.Tasks.FindAsync(s.TaskId);
        await TryReserveRestoredStageEquipmentAsync(db, s, task?.ProjectId);
        AddAdminLog(db, ActivityActionKind.Restored, $"Восстановил этап «{s.Name}» из архива", "Stage", s.Id);
        await db.SaveChangesAsync(); await LoadArchiveAsync(); SetStatus($"Этап «{s.Name}» восстановлен");
    }

    private static bool ShouldReserveStageEquipment(LocalTaskStage stage) =>
        !stage.IsArchived &&
        !stage.IsMarkedForDeletion &&
        stage.Status != StageStatus.Completed;

    private async Task TryReserveRestoredStageEquipmentAsync(LocalDbContext db, LocalTaskStage stage, Guid? projectId)
    {
        if (!ShouldReserveStageEquipment(stage))
            return;

        var eqIds = await db.StageEquipments
            .Where(x => x.StageId == stage.Id)
            .Select(x => x.EquipmentId)
            .Distinct()
            .ToListAsync();

        foreach (var eqId in eqIds)
        {
            var eq = await db.Equipments.FindAsync(eqId);
            if (eq is null || eq.IsWrittenOff)
                continue;

            // Не перехватываем оборудование, если оно уже закреплено за другой задачей.
            if (eq.CheckedOutTaskId.HasValue && eq.CheckedOutTaskId != stage.TaskId)
                continue;

            if ((eq.Status == "InUse" || eq.Status == "Unavailable") && eq.CheckedOutTaskId == stage.TaskId)
                continue;

            var prevStatus = eq.Status;
            eq.Status = "Unavailable";
            eq.CheckedOutTaskId = stage.TaskId;
            eq.CheckedOutProjectId = projectId;
            eq.UpdatedAt = DateTime.UtcNow;
            eq.IsSynced = false;

            db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
            {
                Id = Guid.NewGuid(),
                EquipmentId = eq.Id,
                OccurredAt = DateTime.UtcNow,
                EventType = "CheckedOut",
                PreviousStatus = prevStatus,
                NewStatus = "Unavailable",
                ProjectId = projectId,
                TaskId = stage.TaskId,
                UserId = _auth.UserId,
                UserName = _auth.UserName,
                Comment = $"Выдано на этап: {stage.Name}"
            });

            await _sync.QueueOperationAsync("EquipmentHistory", eq.Id, SyncOperation.Create,
                new RecordEquipmentEventRequest(
                    EventType: EquipmentHistoryEventType.CheckedOut,
                    NewStatus: EquipmentStatus.Unavailable,
                    ProjectId: projectId,
                    TaskId: stage.TaskId,
                    Comment: $"Выдано на этап: {stage.Name}"));
        }
    }

    [RelayCommand]
    private void OpenPermanentDeleteStageConfirm(ArchiveRow? row)
    {
        if (row is null) return;
        SetupConfirm("Удалить навсегда?", row.Name, "Удалить навсегда", destructive: true, async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var s = await db.TaskStages.FindAsync(row.Id); if (s is null) return;
            AddAdminLog(db, ActivityActionKind.PermanentlyDeleted, $"Удалил навсегда этап «{s.Name}»", "Stage", s.Id);
            db.TaskStages.Remove(s); await db.SaveChangesAsync(); await LoadArchiveAsync();
            SetStatus($"Этап «{s.Name}» удалён навсегда");
        });
    }

    [RelayCommand]
    private void OpenRestoreStageConfirm(ArchiveRow? row)
    {
        if (row is null) return;
        SetupConfirm("Восстановить этап?", row.Name, "Восстановить", destructive: false,
            () => RestoreStageAsync(row));
    }

    // ══════════════════════════════════════════════════════════════════════
    // HISTORY TAB
    // ══════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> ActivityKinds = new()
    {
        ActivityActionKind.Login, ActivityActionKind.Logout,
        ActivityActionKind.PasswordChanged, ActivityActionKind.AvatarChanged
    };

    private async Task LoadHistoryAsync()
    {
        _historyLoadedCount = PageSize;
        await using var db = await _dbFactory.CreateDbContextAsync();
        _allHistoryLogs = await db.ActivityLogs
            .Where(l => l.ActionType == null || !ActivityKinds.Contains(l.ActionType))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        var historyUserIds = _allHistoryLogs.Where(l => l.UserId.HasValue).Select(l => l.UserId!.Value).Distinct().ToList();
        if (historyUserIds.Count > 0)
        {
            var historyUserAvatars = await db.Users.Where(u => historyUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToListAsync();
            var avDict = historyUserAvatars.ToDictionary(u => u.Id);
            foreach (var l in _allHistoryLogs)
            {
                if (l.UserId.HasValue && avDict.TryGetValue(l.UserId.Value, out var av))
                {
                    l.AvatarData = av.AvatarData;
                    l.AvatarPath = av.AvatarPath;
                }
            }
        }

        var userNames = _allHistoryLogs.Select(l => l.UserName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
        Application.Current.Dispatcher.Invoke(() =>
        {
            HistoryTotalCount = _allHistoryLogs.Count;
            var currentUser   = HistoryUserFilter;
            var currentAction = HistoryActionFilter;
            HistoryUserList.Clear();
            HistoryUserList.Add("Все");
            foreach (var n in userNames) HistoryUserList.Add(n);
            // Restore selections (prevents reset on reload)
            HistoryUserFilter   = HistoryUserList.Contains(currentUser)   ? currentUser   : "Все";
            HistoryActionFilter = HistoryActionOptions.Contains(currentAction) ? currentAction : "Все";
            ApplyHistoryFilter();
        });
    }

    partial void OnHistorySearchTextChanged(string value)   => ApplyHistoryFilter();
    partial void OnHistoryActionFilterChanged(string value) => ApplyHistoryFilter();
    partial void OnHistoryUserFilterChanged(string value)   => ApplyHistoryFilter();

    private void ApplyHistoryFilter()
    {
        var query = _allHistoryLogs.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(HistorySearchText))
        {
            var s = HistorySearchText.Trim().ToLowerInvariant();
            query = query.Where(l => l.ActionText.ToLowerInvariant().Contains(s) ||
                                     l.UserName.ToLowerInvariant().Contains(s));
        }
        if (HistoryActionFilter != "Все" && HistoryActionMap.TryGetValue(HistoryActionFilter, out var kinds))
            query = query.Where(l => l.ActionType != null && kinds.Contains(l.ActionType));
        if (HistoryUserFilter != "Все")
            query = query.Where(l => l.UserName == HistoryUserFilter);

        var filtered = query.ToList();
        HasMoreHistory = filtered.Count > _historyLoadedCount;
        var page = filtered.Take(_historyLoadedCount).ToList();
        HistoryLogs.Clear();
        foreach (var l in page) HistoryLogs.Add(l);
        FilteredHistoryLogs.Clear();
        foreach (var l in page) FilteredHistoryLogs.Add(l);
    }

    [RelayCommand]
    private void LoadMoreHistory()
    {
        _historyLoadedCount += PageSize;
        ApplyHistoryFilter();
    }

    [RelayCommand]
    private void OpenClearHistoryConfirm()
    {
        SetupConfirm(
            "Очистить историю?",
            "Все записи истории действий будут удалены навсегда",
            "Очистить",
            destructive: true,
            async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                await db.ActivityLogs.Where(l => l.ActionType == null || !ActivityKinds.Contains(l.ActionType)).ExecuteDeleteAsync();
                await LoadHistoryAsync(); SetStatus("История действий очищена");
            });
    }

    // ══════════════════════════════════════════════════════════════════════
    // ACTIVITY TAB
    // ══════════════════════════════════════════════════════════════════════

    private async Task LoadActivityAsync()
    {
        _activityLoadedCount = PageSize;
        await using var db = await _dbFactory.CreateDbContextAsync();
        _allActivityLogs = await db.ActivityLogs
            .Where(l => l.ActionType != null && ActivityKinds.Contains(l.ActionType))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        var activityUserIds = _allActivityLogs
            .Where(l => l.UserId.HasValue)
            .Select(l => l.UserId!.Value)
            .Distinct()
            .ToList();
        if (activityUserIds.Count > 0)
        {
            var activityUserAvatars = await db.Users.Where(u => activityUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                .ToListAsync();
            var avDict = activityUserAvatars.ToDictionary(u => u.Id);
            foreach (var l in _allActivityLogs)
            {
                if (!l.UserId.HasValue || !avDict.TryGetValue(l.UserId.Value, out var av))
                    continue;

                var data = av.AvatarData;
                if ((data is null || data.Length == 0) && !string.IsNullOrWhiteSpace(av.AvatarPath))
                {
                    var fromFile = AvatarHelper.FileToBytes(av.AvatarPath);
                    if (fromFile is { Length: > 0 })
                        data = fromFile;
                }

                l.AvatarData = data;
                l.AvatarPath = av.AvatarPath;
            }
        }

        var userNames = _allActivityLogs.Select(l => l.UserName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n).ToList();
        Application.Current.Dispatcher.Invoke(() =>
        {
            ActivityTotalCount = _allActivityLogs.Count;
            var currentUser  = ActivityUserFilter;
            var currentEvent = ActivityEventFilter;
            ActivityUserList.Clear();
            ActivityUserList.Add("Все");
            foreach (var n in userNames) ActivityUserList.Add(n);
            ActivityUserFilter  = ActivityUserList.Contains(currentUser)   ? currentUser  : "Все";
            ActivityEventFilter = ActivityEventOptions.Contains(currentEvent) ? currentEvent : "Все";
            ApplyActivityFilter();
        });
    }

    partial void OnActivitySearchTextChanged(string value)  => ApplyActivityFilter();
    partial void OnActivityUserFilterChanged(string value)  => ApplyActivityFilter();
    partial void OnActivityEventFilterChanged(string value) => ApplyActivityFilter();

    private void ApplyActivityFilter()
    {
        var query = _allActivityLogs.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(ActivitySearchText))
        {
            var s = ActivitySearchText.Trim().ToLowerInvariant();
            query = query.Where(l => l.ActionText.ToLowerInvariant().Contains(s) ||
                                     l.UserName.ToLowerInvariant().Contains(s));
        }
        if (ActivityEventFilter != "Все" && ActivityEventMap.TryGetValue(ActivityEventFilter, out var kinds))
            query = query.Where(l => l.ActionType != null && kinds.Contains(l.ActionType));
        if (ActivityUserFilter != "Все")
            query = query.Where(l => l.UserName == ActivityUserFilter);

        var filtered = query.ToList();
        HasMoreActivity = filtered.Count > _activityLoadedCount;
        var page = filtered.Take(_activityLoadedCount).ToList();
        ActivityLogs.Clear();
        foreach (var l in page) ActivityLogs.Add(l);
        FilteredActivityLogs.Clear();
        foreach (var l in page) FilteredActivityLogs.Add(l);
    }

    [RelayCommand]
    private void LoadMoreActivity()
    {
        _activityLoadedCount += PageSize;
        ApplyActivityFilter();
    }

    [RelayCommand]
    private void OpenClearActivityConfirm()
    {
        SetupConfirm(
            "Очистить журнал активности?",
            "Все записи активности будут удалены навсегда",
            "Очистить",
            destructive: true,
            async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                await db.ActivityLogs.Where(l => l.ActionType != null && ActivityKinds.Contains(l.ActionType)).ExecuteDeleteAsync();
                await LoadActivityAsync(); SetStatus("Журнал активности очищен");
            });
    }

    // ══════════════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════════════

    internal void AddAdminLog(LocalDbContext db, string actionType, string text, string entityType, Guid entityId)
    {
        db.ActivityLogs.Add(new LocalActivityLog
        {
            UserId       = _auth.UserId,
            ActorRole    = _auth.UserRole,
            UserName     = _auth.UserName ?? "Администратор",
            UserInitials = AvatarHelper.GetInitials(_auth.UserName ?? "АД"),
            UserColor    = "#C0392B",
            ActionType   = actionType,
            ActionText   = text,
            EntityType   = entityType,
            EntityId     = entityId,
            CreatedAt    = DateTime.UtcNow
        });
    }

    public static async Task UpdatePasswordHashAsync(LocalDbContext db, Guid userId, string hash)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is not null) user.PasswordHash = hash;
    }

    [RelayCommand]
    private void SwitchArchiveTab(string tab) => ArchiveTab = tab;

    internal IDbContextFactory<LocalDbContext> DbFactory => _dbFactory;
    internal IAuthService Auth => _auth;
}
