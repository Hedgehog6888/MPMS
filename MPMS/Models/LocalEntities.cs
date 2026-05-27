using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MPMS.Services;

namespace MPMS.Models;

/// <summary>Базовый класс для всех локальных сущностей — отслеживает состояние офлайн синхронизации</summary>
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
    /// <summary>Полное имя — хранится для совместимости. Для новых данных предпочтительнее FirstName+LastName.</summary>
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string FirstName { get; set; } = string.Empty;
    [MaxLength(50)] public string LastName { get; set; } = string.Empty;
    [MaxLength(50)] public string Username { get; set; } = string.Empty;
    [MaxLength(255)] public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    [MaxLength(500)] public string? HomeAddress { get; set; }
    public Guid RoleId { get; set; }
    [MaxLength(50)] public string RoleName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    [MaxLength(500)] public string? AvatarPath { get; set; }

    public byte[]? AvatarData { get; set; }

    public string? PasswordHash { get; set; }

    [MaxLength(100)] public string? SubRole { get; set; }

    public string? AdditionalSubRoles { get; set; }

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

    /// <summary>Базовая роль на русском (всегда «Работник» для Worker — для фильтров и списка администраторов).</summary>
    [NotMapped]
    public string RoleDisplayName => RoleName switch
    {
        "Administrator" or "Admin" => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager" => "Менеджер",
        "Foreman" => "Прораб",
        "Worker" => "Работник",
        { Length: > 0 } r => r,
        _ => "—"
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

    /// <summary>Проект был мягко удалён (перемещён в архив). Отдельно от IsMarkedForDeletion.</summary>
    public bool IsArchived { get; set; } = false;
    public bool IsClosed { get; set; } = false;

    public DateTime? ClosedAt { get; set; }
    public string? ClosureReason { get; set; }

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
    [NotMapped]
    public string ManagerInitials => string.IsNullOrWhiteSpace(ManagerName) ? "?" :
        string.Join("", ManagerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(w => w[0].ToString().ToUpper()));

    [NotMapped] public byte[]? ManagerAvatarData { get; set; }
    [NotMapped] public string? ManagerAvatarPath { get; set; }

    [NotMapped]
    public bool IsOverdue => EndDate.HasValue
        && EndDate < DateOnly.FromDateTime(DateTime.Today)
        && Status != ProjectStatus.Completed;
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
    [NotMapped]
    public string AssignedUserInitials => string.IsNullOrWhiteSpace(AssignedUserName) ? "?"
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

    [NotMapped] public bool ProjectIsMarkedForDeletion { get; set; }

    [NotMapped]
    public bool EffectiveTaskMarkedForDeletion =>
        IsMarkedForDeletion || ProjectIsMarkedForDeletion;

    [NotMapped]
    public DeletionMarkSource TaskDeletionMarkSource =>
        ProjectIsMarkedForDeletion ? DeletionMarkSource.Project :
        IsMarkedForDeletion ? DeletionMarkSource.Task : DeletionMarkSource.None;

    /// <summary>Кнопка пометки задачи скрыта, пока проект помечен — снимать только с проекта.</summary>
    [NotMapped] public bool CanToggleTaskDeletionMark => TaskDeletionMarkSource != DeletionMarkSource.Project;

    [NotMapped]
    public string TaskInheritedDeletionHint =>
        TaskDeletionMarkSource == DeletionMarkSource.Project
            ? "Пометка с уровня проекта"
            : "";

    /// <summary>Задача была мягко удалена (перемещена в архив). Отдельно от IsMarkedForDeletion.</summary>
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
    public Guid? WorkTypeTemplateId { get; set; }
    [MaxLength(200)] public string? WorkTypeNameSnapshot { get; set; }
    public string? WorkTypeDescriptionSnapshot { get; set; }
    [MaxLength(50)] public string? WorkUnitSnapshot { get; set; }
    public decimal WorkQuantity { get; set; }
    public decimal WorkPricePerUnit { get; set; }
    public Guid? AssignedUserId { get; set; }
    [MaxLength(100)] public string? AssignedUserName { get; set; }
    [NotMapped] public byte[]? AssignedUserAvatarData { get; set; }
    [NotMapped] public string? AssignedUserAvatarPath { get; set; }
    [NotMapped]
    public string AssignedUserInitials => string.IsNullOrWhiteSpace(AssignedUserName) ? "?"
        : string.Join("", AssignedUserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));
    public StageStatus Status { get; set; } = StageStatus.Planned;
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;

    [NotMapped] public bool TaskIsMarkedForDeletion { get; set; }
    [NotMapped] public bool ProjectIsMarkedForDeletion { get; set; }

    [NotMapped]
    public bool EffectiveMarkedForDeletion =>
        IsMarkedForDeletion || TaskIsMarkedForDeletion || ProjectIsMarkedForDeletion;

    [NotMapped]
    public DeletionMarkSource StageDeletionMarkSource =>
        ProjectIsMarkedForDeletion ? DeletionMarkSource.Project :
        TaskIsMarkedForDeletion ? DeletionMarkSource.Task :
        IsMarkedForDeletion ? DeletionMarkSource.Stage : DeletionMarkSource.None;

    [NotMapped]
    public bool CanToggleStageDeletionMark =>
        StageDeletionMarkSource is DeletionMarkSource.None or DeletionMarkSource.Stage;

    [NotMapped]
    public string StageInheritedDeletionHint => StageDeletionMarkSource switch
    {
        DeletionMarkSource.Project => "Пометка с уровня проекта",
        DeletionMarkSource.Task => "Пометка с уровня задачи",
        _ => ""
    };

    /// <summary>Этап был мягко удалён (перемещён в архив). Отдельно от IsMarkedForDeletion.</summary>
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

    [NotMapped] public int Count { get; set; }
}

public class LocalWorkTypeCategory
{
    public Guid Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(500)] public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    [NotMapped] public int WorkTypeCount { get; set; }
}

public class LocalWorkTypeTemplate : LocalEntity
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

    [NotMapped] public int Count { get; set; }
}

public class LocalMaterial : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)] public string? Unit { get; set; }
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
        "Available" => "Доступно",
        "Unavailable" => "Недоступно",
        "3" => "Недоступно",
        "InUse" => "Используется",
        "CheckedOut" => "Используется",
        "Retired" => "Списано",
        _ => Status
    };

    [NotMapped]
    public string StatusColor => Status switch
    {
        "Available" => "#00875A",
        "Unavailable" => "#DE350B",
        "3" => "#DE350B",
        "InUse" => "#FF8B00",
        "CheckedOut" => "#FF8B00",
        "Retired" => "#6B778C",
        "WrittenOff" => "#6B778C",
        _ => "#6B778C"
    };

    [NotMapped]
    public string ConditionDisplay => Condition switch
    {
        "Good" => "Исправно",
        "NeedsMaintenance" => "Требует обслуживания",
        "Faulty" => "Неисправно",
        _ => Condition
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
        "Added" => "Добавлено",
        "CheckedOut" => "Выдано",
        "Returned" => "Возвращено",
        "ConditionChanged" => "Смена состояния",
        "StatusChanged" => "Смена статуса",
        "StatusChange" => "Смена статуса",
        "Note" => "Заметка",
        "WrittenOff" => "Списано",
        _ => EventType
    };

    [NotMapped]
    public string HistoryIconBackground => EventType switch
    {
        "CheckedOut" or "WrittenOff" => "#FFEBE6",
        "Returned" or "Added" => "#E3FCEF",
        "ConditionChanged" when NewStatus == "NeedsMaintenance" => "#DEEBFF",
        "ConditionChanged" when NewStatus == "Faulty" => "#FFEBE6",
        "ConditionChanged" => "#E3FCEF",
        _ => "#E3FCEF"
    };

    [NotMapped]
    public string HistoryIconPath => EventType switch
    {
        "CheckedOut" => "/icons/history_minus.svg",
        "WrittenOff" => "/icons/history_close.svg",
        "Returned" or "Added" => "/icons/history_plus.svg",
        "ConditionChanged" when NewStatus == "NeedsMaintenance" => "/icons/history_gear.svg",
        "ConditionChanged" when NewStatus == "Faulty" => "/icons/history_alert.svg",
        "ConditionChanged" => "/icons/history_check.svg",
        _ => "/icons/history_plus.svg"
    };
}

public class LocalStageMaterial : LocalEntity
{
    public Guid StageId { get; set; }
    public Guid MaterialId { get; set; }
    [MaxLength(200)] public string MaterialName { get; set; } = string.Empty;
    [MaxLength(50)] public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
    [NotMapped] public string StageName { get; set; } = string.Empty;
}

public class LocalStageWorkType : LocalEntity
{
    public Guid StageId { get; set; }
    public Guid WorkTypeTemplateId { get; set; }
    [MaxLength(200)] public string WorkTypeName { get; set; } = string.Empty;
    public string? WorkTypeDescription { get; set; }
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

public class LocalFile : LocalEntity, INotifyPropertyChanged
{
    [MaxLength(255)] public string FileName { get; set; } = string.Empty;
    [MaxLength(1000)] public string FilePath { get; set; } = string.Empty;
    [MaxLength(100)] public string? FileType { get; set; }
    public long FileSize { get; set; }

    private byte[]? _fileData;
    public byte[]? FileData
    {
        get => _fileData;
        set
        {
            if (!ReferenceEquals(_fileData, value))
            {
                _fileData = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileData)));
            }
        }
    }

    public Guid UploadedById { get; set; }
    [MaxLength(100)] public string UploadedByName { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? StageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? OriginalCreatedAt { get; set; }
    public string? Description { get; set; }

    [NotMapped] public string? ProjectName { get; set; }
    [NotMapped] public string? StageName { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Участник проекта — пользователи назначенные на проект (исполнители).</summary>
public class LocalProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(50)] public string UserRole { get; set; } = string.Empty;

    [NotMapped] public string? AvatarPath { get; set; }

    [NotMapped] public byte[]? AvatarData { get; set; }

    [NotMapped] public string? SubRole { get; set; }

    [NotMapped] public string? AdditionalSubRolesJson { get; set; }

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

    [NotMapped] public bool IsUserPeekInteractive { get; set; }
}

/// <summary>Исполнитель задачи — поддерживает несколько исполнителей на задачу.</summary>
public class LocalTaskAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;

    [NotMapped] public string? AvatarPath { get; set; }
    [NotMapped] public byte[]? AvatarData { get; set; }

    [NotMapped] public string? SubRole { get; set; }

    [NotMapped] public string? AdditionalSubRolesJson { get; set; }

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

/// <summary>Исполнитель этапа — только пользователи из исполнителей родительской задачи.</summary>
public class LocalStageAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StageId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;

    [NotMapped] public string? AvatarPath { get; set; }

    [NotMapped] public byte[]? AvatarData { get; set; }

    [NotMapped]
    public string Initials => string.IsNullOrWhiteSpace(UserName) ? "?"
        : string.Join("", UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));
}

/// <summary>Сообщение/комментарий к задаче или проекту.</summary>
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

/// <summary>Тип действия для лога активности — используется для стилизации и фильтрации.</summary>
public static class ActivityActionKind
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    public const string MarkedForDeletion = "MarkedForDeletion";
    public const string UnmarkedForDeletion = "UnmarkedForDeletion";
    public const string Message = "Message";

    // События аутентификации
    public const string Login = "Login";
    public const string Logout = "Logout";

    // События профиля
    public const string PasswordChanged = "PasswordChanged";
    public const string AvatarChanged = "AvatarChanged";

    // События управления пользователями (только администратор)
    public const string UserCreated = "UserCreated";
    public const string UserEdited = "UserEdited";
    public const string UserBlocked = "UserBlocked";
    public const string UserUnblocked = "UserUnblocked";
    public const string UserDeleted = "UserDeleted";

    // Архивация / восстановление
    public const string Restored = "Restored";
    public const string PermanentlyDeleted = "PermanentlyDeleted";

    // Изменения статуса
    public const string StatusChanged = "StatusChanged";
    public const string TaskStatusChanged = "TaskStatusChanged";
    public const string StageStatusChanged = "StageStatusChanged";

    // События участников проекта
    public const string MemberAdded = "MemberAdded";
    public const string MemberRemoved = "MemberRemoved";

    // События материалов/видов работ этапа
    public const string MaterialAdded = "MaterialAdded";
    public const string MaterialRemoved = "MaterialRemoved";
    public const string WorkTypeAdded = "WorkTypeAdded";
    public const string WorkTypeRemoved = "WorkTypeRemoved";
}

/// <summary>Запись локального лога активности — отслеживает действия пользователей для ленты активности.</summary>
public class LocalActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Пользователь выполнивший действие — используется для фильтрации по ролям.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Роль действующего лица на момент лога — используется для скрытия действий администратора от менеджеров.</summary>
    [MaxLength(50)] public string? ActorRole { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)] public string UserInitials { get; set; } = "?";
    [MaxLength(20)] public string UserColor { get; set; } = "#0F2038";
    [MaxLength(50)] public string? ActionType { get; set; }
    [MaxLength(500)] public string ActionText { get; set; } = string.Empty;
    public string? DetailsText { get; set; }
    [MaxLength(50)] public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [NotMapped] public string ActivityTooltipText => string.IsNullOrWhiteSpace(DetailsText) ? ActionText : DetailsText;
    [NotMapped] public string ActivityTooltipTitle => ActivityDetailsService.GetTooltipTitle(this);
    [NotMapped] public string ActivityTooltipActionLabel => ActivityDetailsService.GetActionDisplay(ActionType);
    [NotMapped] public string ActivityTooltipEntityLabel => ActivityDetailsService.GetEntityDisplay(EntityType);
    [NotMapped] public IReadOnlyList<string> ActivityTooltipDetailLines => ActivityDetailsService.GetTooltipDetailLines(this);

    [NotMapped] public int GroupCount { get; set; } = 1;

    [NotMapped] public byte[]? AvatarData { get; set; }
    [NotMapped] public string? AvatarPath { get; set; }
}

/// <summary>ID пользователей удалённых локально — предотвращает повторное добавление при синхронизации.</summary>
public class DeletedUserId
{
    public Guid Id { get; set; }
}

/// <summary>Хранит JWT токен и информацию о текущем пользователе между сессиями</summary>
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

    public string? SessionPasswordProtected { get; set; }

    public string RefreshToken { get; set; } = string.Empty;

    public string LocalPasswordHash { get; set; } = string.Empty;

    public bool IsActiveSession { get; set; } = true;
}

/// <summary>Хранит последние N аккаунтов которые вошли — отображается в окне входа</summary>
public class RecentAccount
{
    public int Id { get; set; }
    [MaxLength(50)] public string Username { get; set; } = string.Empty;
    [MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(50)] public string Role { get; set; } = string.Empty;
    [MaxLength(20)] public string AvatarColor { get; set; } = "#0F2038";
    [MaxLength(5)] public string Initials { get; set; } = "?";
    public DateTime LastLoginAt { get; set; }

    /// <summary>Вычисляет инициалы и цвет из имени и роли</summary>
    public static RecentAccount From(string username, string displayName, string role)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : displayName.Length > 0 ? $"{displayName[0]}" : "?";

        var color = role switch
        {
            "Administrator" => "#C0392B",
            "Project Manager" => "#2980B9",
            "Foreman" => "#27AE60",
            "Worker" => "#E67E22",
            _ => "#0F2038"
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

public class LocalNote : LocalEntity
{
    public Guid UserId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Содержимое форматированного текста хранится как XAML строка.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Без названия" : Title;

    [NotMapped]
    public string DisplayDate => UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
