using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MPMS.Services;

namespace MPMS.Models;

/// <summary>Base for all local entities — tracks offline sync state</summary>
public abstract class LocalEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsSynced { get; set; } = false;
    public DateTime LastModifiedLocally { get; set; } = DateTime.UtcNow;
}

public class LocalRole
{
    public Guid Id { get; set; }
    [MaxLength(50)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class LocalUser : LocalEntity
{
    /// <summary>Full name — stored for compatibility. Prefer FirstName+LastName for new data.</summary>
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string FirstName { get; set; } = string.Empty;
    [MaxLength(50)] public string LastName  { get; set; } = string.Empty;
    [MaxLength(50)] public string Username { get; set; } = string.Empty;
    [MaxLength(255)] public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    [MaxLength(500)] public string? HomeAddress { get; set; }
    public Guid RoleId { get; set; }
    [MaxLength(50)]  public string RoleName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    [MaxLength(500)] public string? AvatarPath { get; set; }

    /// <summary>Avatar stored as PNG bytes in the database (takes priority over AvatarPath).</summary>
    public byte[]? AvatarData { get; set; }

    /// <summary>BCrypt hash for offline login — set by admin when creating/editing the user locally.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Primary worker specialty (e.g. "Электромонтажник").</summary>
    [MaxLength(100)] public string? SubRole { get; set; }

    /// <summary>JSON array of additional specialties (see WorkerSpecialtiesJson).</summary>
    public string? AdditionalSubRoles { get; set; }

    /// <summary>Indicates the account is blocked — user cannot log in.</summary>
    public bool IsBlocked { get; set; } = false;
    public DateTime? BlockedAt { get; set; }
    [MaxLength(500)] public string? BlockedReason { get; set; }

    [NotMapped]
    public string Initials
    {
        get
        {
            var n = !string.IsNullOrWhiteSpace(Name) ? Name : $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(n) ? "?"
                : string.Join("", n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? char.ToUpper(w[0]).ToString() : ""));
        }
    }

    /// <summary>Base role in Russian (always «Работник» for Worker — for filters and admin list).</summary>
    [NotMapped]
    public string RoleDisplayName => RoleName switch
    {
        "Administrator" or "Admin"                              => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager"     => "Менеджер",
        "Foreman"                                               => "Прораб",
        "Worker"                                                => "Работник",
        { Length: > 0 } r                                       => r,
        _                                                       => "—"
    };

    [NotMapped]
    public string WorkerLabel => RoleName is "Worker"
        ? WorkerSpecialtiesJson.FormatWorkerLine(SubRole, AdditionalSubRoles)
        : RoleDisplayName;
}

public class LocalProject : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [MaxLength(200)] public string? Client { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    public Guid ManagerId { get; set; }
    [MaxLength(100)] public string ManagerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;

    /// <summary>Project has been soft-deleted (moved to archive). Separate from IsMarkedForDeletion.</summary>
    public bool IsArchived { get; set; } = false;

    [NotMapped] public int TotalTasks { get; set; }
    [NotMapped] public int CompletedTasks { get; set; }
    [NotMapped] public int InProgressTasks { get; set; }
    [NotMapped] public int PausedTasks { get; set; }
    [NotMapped] public int TotalStages { get; set; }
    [NotMapped] public int CompletedStages { get; set; }
    [NotMapped] public int InProgressStages { get; set; }
    [NotMapped] public int OverdueTasks { get; set; }
    [NotMapped] public double AverageTaskProgress { get; set; }
    /// <summary>ProgressCalculator: прогресс проекта учитывает задачи, этапы, просрочку и средний прогресс.</summary>
    [NotMapped] public int ProgressPercent => ProgressCalculator.GetProjectProgressPercent(this);
    [NotMapped] public string ManagerInitials => string.IsNullOrWhiteSpace(ManagerName) ? "?" :
        string.Join("", ManagerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(w => w[0].ToString().ToUpper()));

    /// <summary>Manager avatar from Users — populated when loading for display.</summary>
    [NotMapped] public byte[]? ManagerAvatarData { get; set; }
    [NotMapped] public string? ManagerAvatarPath { get; set; }
}

public class LocalTask : LocalEntity
{
    public Guid ProjectId { get; set; }
    [MaxLength(200)] public string ProjectName { get; set; } = string.Empty;
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? AssignedUserId { get; set; }
    [MaxLength(100)] public string? AssignedUserName { get; set; }
    [NotMapped] public byte[]? AssignedUserAvatarData { get; set; }
    [NotMapped] public string? AssignedUserAvatarPath { get; set; }
    [NotMapped] public string AssignedUserInitials => string.IsNullOrWhiteSpace(AssignedUserName) ? "?"
        : string.Join("", AssignedUserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateOnly? DueDate { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Planned;
    public int TotalStages { get; set; }
    public int CompletedStages { get; set; }
    [NotMapped] public int InProgressStages { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;

    /// <summary>Флаг проекта при загрузке (не в БД). Нужен для наследования пометки и прогресса.</summary>
    [NotMapped] public bool ProjectIsMarkedForDeletion { get; set; }

    /// <summary>Пометка к удалению с учётом проекта (задача помечена или проект помечен).</summary>
    [NotMapped] public bool EffectiveTaskMarkedForDeletion =>
        IsMarkedForDeletion || ProjectIsMarkedForDeletion;

    [NotMapped] public DeletionMarkSource TaskDeletionMarkSource =>
        ProjectIsMarkedForDeletion ? DeletionMarkSource.Project :
        IsMarkedForDeletion ? DeletionMarkSource.Task : DeletionMarkSource.None;

    /// <summary>Кнопка пометки задачи скрыта, пока проект помечен — снимать только с проекта.</summary>
    [NotMapped] public bool CanToggleTaskDeletionMark => TaskDeletionMarkSource != DeletionMarkSource.Project;

    [NotMapped] public string TaskInheritedDeletionHint =>
        TaskDeletionMarkSource == DeletionMarkSource.Project
            ? "Пометка с уровня проекта"
            : "";

    /// <summary>Task has been soft-deleted (moved to archive). Separate from IsMarkedForDeletion.</summary>
    public bool IsArchived { get; set; } = false;

    [NotMapped] public int PlannedStages => Math.Max(0, TotalStages - CompletedStages - InProgressStages);
    /// <summary>ProgressCalculator: прогресс задачи учитывает все активные этапы и просрочку.</summary>
    public int ProgressPercent => ProgressCalculator.GetTaskProgressPercent(this);

    public bool IsOverdue => DueDate.HasValue
        && DueDate < DateOnly.FromDateTime(DateTime.Today)
        && Status != TaskStatus.Completed;
}

public class LocalTaskStage : LocalEntity
{
    public Guid TaskId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ServiceTemplateId { get; set; }
    [MaxLength(200)] public string? ServiceNameSnapshot { get; set; }
    public string? ServiceDescriptionSnapshot { get; set; }
    [MaxLength(50)] public string? WorkUnitSnapshot { get; set; }
    public decimal WorkQuantity { get; set; }
    public decimal WorkPricePerUnit { get; set; }
    public Guid? AssignedUserId { get; set; }
    [MaxLength(100)] public string? AssignedUserName { get; set; }
    [NotMapped] public byte[]? AssignedUserAvatarData { get; set; }
    [NotMapped] public string? AssignedUserAvatarPath { get; set; }
    [NotMapped] public string AssignedUserInitials => string.IsNullOrWhiteSpace(AssignedUserName) ? "?"
        : string.Join("", AssignedUserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));
    public StageStatus Status { get; set; } = StageStatus.Planned;
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;

    [NotMapped] public bool TaskIsMarkedForDeletion { get; set; }
    [NotMapped] public bool ProjectIsMarkedForDeletion { get; set; }

    [NotMapped] public bool EffectiveMarkedForDeletion =>
        IsMarkedForDeletion || TaskIsMarkedForDeletion || ProjectIsMarkedForDeletion;

    [NotMapped] public DeletionMarkSource StageDeletionMarkSource =>
        ProjectIsMarkedForDeletion ? DeletionMarkSource.Project :
        TaskIsMarkedForDeletion ? DeletionMarkSource.Task :
        IsMarkedForDeletion ? DeletionMarkSource.Stage : DeletionMarkSource.None;

    [NotMapped] public bool CanToggleStageDeletionMark =>
        StageDeletionMarkSource is DeletionMarkSource.None or DeletionMarkSource.Stage;

    [NotMapped] public string StageInheritedDeletionHint => StageDeletionMarkSource switch
    {
        DeletionMarkSource.Project => "Пометка с уровня проекта",
        DeletionMarkSource.Task => "Пометка с уровня задачи",
        _ => ""
    };

    /// <summary>Stage has been soft-deleted (moved to archive). Separate from IsMarkedForDeletion.</summary>
    public bool IsArchived { get; set; } = false;

    [NotMapped] public string TaskName { get; set; } = string.Empty;

    [NotMapped]
    public bool IsOverdue => DueDate.HasValue
        && DueDate < DateOnly.FromDateTime(DateTime.Today)
        && Status != StageStatus.Completed;
}

public class LocalMaterialCategory
{
    public Guid Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
}

public class LocalServiceCategory
{
    public Guid Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class LocalServiceTemplate : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    [MaxLength(100)] public string? Article { get; set; }
    public decimal BasePrice { get; set; }
    public Guid CategoryId { get; set; }
    [MaxLength(120)] public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class LocalEquipmentCategory
{
    public Guid Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
}

public class LocalMaterial : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)]  public string? Unit { get; set; }
    public string? Description { get; set; }
    public decimal Quantity { get; set; }
    public decimal? Cost { get; set; }
    [MaxLength(100)] public string? InventoryNumber { get; set; }
    public Guid? CategoryId { get; set; }
    [MaxLength(100)] public string? CategoryName { get; set; }
    [MaxLength(500)] public string? ImagePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsWrittenOff { get; set; } = false;
    public DateTime? WrittenOffAt { get; set; }
    [MaxLength(500)] public string? WrittenOffComment { get; set; }
    public bool IsArchived { get; set; } = false;
}

/// <summary>История движения материала (приход/расход) — синхронизируется с сервера.</summary>
public class LocalMaterialStockMovement
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public DateTime OccurredAt { get; set; }
    public decimal Delta { get; set; }
    public decimal QuantityAfter { get; set; }
    [MaxLength(30)] public string OperationType { get; set; } = string.Empty;
    [MaxLength(500)] public string? Comment { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
}

public class LocalEquipment : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    [MaxLength(100)] public string? CategoryName { get; set; }
    [MaxLength(500)] public string? ImagePath { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Available";
    [MaxLength(30)] public string Condition { get; set; } = "Good";
    [MaxLength(100)] public string? InventoryNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CheckedOutProjectId { get; set; }
    public Guid? CheckedOutTaskId { get; set; }
    public bool IsWrittenOff { get; set; } = false;
    public DateTime? WrittenOffAt { get; set; }
    [MaxLength(500)] public string? WrittenOffComment { get; set; }
    public bool IsArchived { get; set; } = false;

    [NotMapped]
    public string StatusDisplay => Status switch
    {
        "Available"   => "Доступно",
        "Unavailable" => "Недоступно",
        "3"           => "Недоступно",
        "InUse"       => "Используется",
        "CheckedOut"  => "Используется",
        "Retired"     => "Списано",
        _             => Status
    };

    [NotMapped]
    public string StatusColor => Status switch
    {
        "Available"   => "#00875A",
        "Unavailable" => "#DE350B",
        "3"           => "#DE350B",
        "InUse"       => "#FF8B00",
        "CheckedOut"  => "#FF8B00",
        "Retired"     => "#6B778C",
        "WrittenOff"  => "#6B778C",
        _             => "#6B778C"
    };

    [NotMapped]
    public string ConditionDisplay => Condition switch
    {
        "Good"              => "Исправно",
        "NeedsMaintenance"  => "Требует обслуживания",
        "Faulty"            => "Неисправно",
        _                   => Condition
    };
}

/// <summary>История оборудования: выдача, возврат, смена статуса.</summary>
public class LocalEquipmentHistoryEntry
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public DateTime OccurredAt { get; set; }
    [MaxLength(30)] public string EventType { get; set; } = string.Empty;
    [MaxLength(30)] public string? PreviousStatus { get; set; }
    [MaxLength(30)] public string? NewStatus { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? UserId { get; set; }
    [MaxLength(100)] public string? UserName { get; set; }
    [MaxLength(500)] public string? Comment { get; set; }

    [NotMapped]
    public string EventTypeDisplay => EventType switch
    {
        "Added"        => "Добавлено",
        "CheckedOut"   => "Выдано",
        "Returned"     => "Возвращено",
        "StatusChanged" => "Смена статуса",
        "StatusChange" => "Смена статуса",
        "Note"         => "Заметка",
        "WrittenOff"   => "Списано",
        _              => EventType
    };
}

public class LocalStageMaterial : LocalEntity
{
    public Guid StageId { get; set; }
    public Guid MaterialId { get; set; }
    [MaxLength(200)] public string MaterialName { get; set; } = string.Empty;
    [MaxLength(50)]  public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    [NotMapped] public string StageName { get; set; } = string.Empty;
}

public class LocalStageService : LocalEntity
{
    public Guid StageId { get; set; }
    public Guid ServiceTemplateId { get; set; }
    [MaxLength(200)] public string ServiceName { get; set; } = string.Empty;
    public string? ServiceDescription { get; set; }
    [MaxLength(50)] public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
}

public class LocalStageEquipment : LocalEntity
{
    public Guid StageId { get; set; }
    public Guid EquipmentId { get; set; }
    [MaxLength(200)] public string EquipmentName { get; set; } = string.Empty;
    [MaxLength(100)] public string? InventoryNumber { get; set; }
}

public class LocalFile : LocalEntity
{
    [MaxLength(255)]  public string FileName { get; set; } = string.Empty;
    [MaxLength(1000)] public string FilePath { get; set; } = string.Empty;
    [MaxLength(100)]  public string? FileType { get; set; }
    public long FileSize { get; set; }
    public byte[]? FileData { get; set; }
    public Guid UploadedById { get; set; }
    [MaxLength(100)] public string UploadedByName { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? StageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? OriginalCreatedAt { get; set; }
    
    [NotMapped] public string? ProjectName { get; set; }
    [NotMapped] public string? StageName { get; set; }
}

/// <summary>Project member — users assigned to a project (executors).</summary>
public class LocalProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(50)] public string UserRole { get; set; } = string.Empty;

    /// <summary>Avatar path from LocalUser — populated when loading members.</summary>
    [NotMapped] public string? AvatarPath { get; set; }

    /// <summary>Avatar PNG bytes from LocalUser — takes priority over AvatarPath.</summary>
    [NotMapped] public byte[]? AvatarData { get; set; }

    /// <summary>Primary specialty — populated from LocalUser when loading.</summary>
    [NotMapped] public string? SubRole { get; set; }

    /// <summary>JSON additional specialties — populated from LocalUser.</summary>
    [NotMapped] public string? AdditionalSubRolesJson { get; set; }

    /// <summary>Подпись под именем только у работников (компактная специализация).</summary>
    [NotMapped]
    public string RoleLabel => UserRole is "Worker" or "Работник"
        ? WorkerSpecialtiesJson.FormatWorkerLineCompact(SubRole, AdditionalSubRolesJson)
        : "";

    /// <summary>Цвет подписи работника (#RRGGBB) для HexToBrush.</summary>
    [NotMapped]
    public string WorkerLineForegroundHex => UserRole is "Worker" or "Работник"
        ? WorkerSpecialtiesJson.ForegroundHexForWorkerLine(SubRole, AdditionalSubRolesJson)
        : "#6B778C";

    [NotMapped]
    public string Initials => string.IsNullOrWhiteSpace(UserName) ? "?"
        : string.Join("", UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));

    /// <summary>Клик по строке открывает карточку участника (задаётся при загрузке состава).</summary>
    [NotMapped] public bool IsUserPeekInteractive { get; set; }
}

/// <summary>Task assignee — supports multiple assignees per task.</summary>
public class LocalTaskAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;

    /// <summary>Avatar path from LocalUser — populated when loading.</summary>
    [NotMapped] public string? AvatarPath { get; set; }

    /// <summary>Avatar PNG bytes from LocalUser — takes priority over AvatarPath.</summary>
    [NotMapped] public byte[]? AvatarData { get; set; }

    /// <summary>Primary specialty — populated from LocalUser when loading.</summary>
    [NotMapped] public string? SubRole { get; set; }

    /// <summary>JSON additional specialties — populated from LocalUser.</summary>
    [NotMapped] public string? AdditionalSubRolesJson { get; set; }

    /// <summary>Role name for display — populated from LocalUser when loading.</summary>
    [NotMapped] public string? RoleName { get; set; }

    [NotMapped]
    public string RoleLabel => RoleName is "Worker" or "Работник"
        ? WorkerSpecialtiesJson.FormatWorkerLineCompact(SubRole, AdditionalSubRolesJson)
        : "";

    [NotMapped]
    public string WorkerLineForegroundHex => RoleName is "Worker" or "Работник"
        ? WorkerSpecialtiesJson.ForegroundHexForWorkerLine(SubRole, AdditionalSubRolesJson)
        : "#6B778C";

    [NotMapped]
    public string Initials => string.IsNullOrWhiteSpace(UserName) ? "?"
        : string.Join("", UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));

    [NotMapped] public bool IsUserPeekInteractive { get; set; }
}

/// <summary>Stage assignee — only users from parent task's assignees.</summary>
public class LocalStageAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StageId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;

    /// <summary>Avatar path from LocalUser — populated when loading.</summary>
    [NotMapped] public string? AvatarPath { get; set; }

    /// <summary>Avatar PNG bytes from LocalUser — takes priority over AvatarPath.</summary>
    [NotMapped] public byte[]? AvatarData { get; set; }

    [NotMapped]
    public string Initials => string.IsNullOrWhiteSpace(UserName) ? "?"
        : string.Join("", UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));
}

/// <summary>Message/comment on a task or project.</summary>
public class LocalMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TaskId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)] public string UserInitials { get; set; } = "?";
    [MaxLength(20)] public string UserColor { get; set; } = "#0F2038";
    [MaxLength(50)] public string UserRole { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped] public byte[]? AvatarData { get; set; }
    [NotMapped] public string? AvatarPath { get; set; }
}

/// <summary>Action type for activity log — used for styling and filtering.</summary>
public static class ActivityActionKind
{
    public const string Created              = "Created";
    public const string Updated              = "Updated";
    public const string Deleted              = "Deleted";
    public const string MarkedForDeletion    = "MarkedForDeletion";
    public const string UnmarkedForDeletion  = "UnmarkedForDeletion";
    public const string Message              = "Message";

    // Auth events
    public const string Login                = "Login";
    public const string Logout               = "Logout";

    // Profile events
    public const string PasswordChanged      = "PasswordChanged";
    public const string AvatarChanged        = "AvatarChanged";

    // Admin-only user management events
    public const string UserCreated          = "UserCreated";
    public const string UserEdited           = "UserEdited";
    public const string UserBlocked          = "UserBlocked";
    public const string UserUnblocked        = "UserUnblocked";
    public const string UserDeleted          = "UserDeleted";

    // Archive / restore
    public const string Restored             = "Restored";
    public const string PermanentlyDeleted   = "PermanentlyDeleted";
}

/// <summary>Local activity log entry — tracks user actions for the activity feed.</summary>
public class LocalActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>User who performed the action — used for role-based filtering.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Role of the actor at log time — used to hide admin actions from managers.</summary>
    [MaxLength(50)] public string? ActorRole { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)]   public string UserInitials { get; set; } = "?";
    [MaxLength(20)]  public string UserColor { get; set; } = "#0F2038";
    [MaxLength(50)]  public string? ActionType { get; set; }
    [MaxLength(500)] public string ActionText { get; set; } = string.Empty;
    [MaxLength(50)]  public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Avatar from Users — populated when loading for display.</summary>
    [NotMapped] public byte[]? AvatarData { get; set; }
    [NotMapped] public string? AvatarPath { get; set; }
}

/// <summary>IDs of users deleted locally — prevents sync from re-adding them.</summary>
public class DeletedUserId
{
    public Guid Id { get; set; }
}

/// <summary>Stores the JWT token and current user info between sessions</summary>
public class AuthSession
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string ApiBaseUrl { get; set; } = "http://localhost:5147/";

    /// <summary>DPAPI (CurrentUser) — для повторного LoginAsync и JWT после перезапуска без ввода пароля.</summary>
    public string? SessionPasswordProtected { get; set; }

    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the last entered password — allows offline login.</summary>
    public string LocalPasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// True while the user is actively logged in.
    /// Set to false on logout (records are kept so any cached account can re-login offline).
    /// </summary>
    public bool IsActiveSession { get; set; } = true;
}

/// <summary>Stores the last N accounts that logged in — shown in the login window</summary>
public class RecentAccount
{
    public int Id { get; set; }
    [MaxLength(50)]  public string Username { get; set; } = string.Empty;
    [MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(50)]  public string Role { get; set; } = string.Empty;
    [MaxLength(20)]  public string AvatarColor { get; set; } = "#0F2038";
    [MaxLength(5)]   public string Initials { get; set; } = "?";
    public DateTime LastLoginAt { get; set; }

    /// <summary>Derive initials and color from name and role</summary>
    public static RecentAccount From(string username, string displayName, string role)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : displayName.Length > 0 ? $"{displayName[0]}" : "?";

        var color = role switch
        {
            "Administrator"   => "#C0392B",
            "Project Manager" => "#2980B9",
            "Foreman"         => "#27AE60",
            "Worker"          => "#E67E22",
            _                 => "#0F2038"
        };

        return new RecentAccount
        {
            Username = username,
            DisplayName = displayName,
            Role = role,
            AvatarColor = color,
            Initials = initials.ToUpper(),
            LastLoginAt = DateTime.UtcNow
        };
    }
}
