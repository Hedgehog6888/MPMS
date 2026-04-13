namespace MPMS.Models;

// ── Auth ──────────────────────────────────────────────────────────────────────
public record LoginRequest(string Username, string Password);

/// <summary>Result of a login attempt — distinguishes network errors from wrong credentials.</summary>
public record LoginResult(AuthResponse? Response, string? Error)
{
    public bool Success => Response is not null;
    public static LoginResult Ok(AuthResponse r)           => new(r, null);
    public static LoginResult Fail(string error)           => new(null, error);
    public static LoginResult Offline()                    => Fail("Нет соединения с сервером. Проверьте, что API запущен (порт 5147).");
    public static LoginResult WrongCredentials()           => Fail("Неверный логин или пароль.");
    public static LoginResult Blocked(string message)      => Fail(message);
}
public record RegisterRequest(string FirstName, string LastName, string Username, string? Email, string Password, Guid RoleId);
public record AuthResponse(Guid UserId, string Name, string Username, string Role,
    string Token, DateTime ExpiresAt);
public record RoleDto(Guid Id, string Name, string? Description);

// ── Projects ──────────────────────────────────────────────────────────────────
public record CreateProjectRequest(string Name, string? Description, string? Client,
    string? Address, DateOnly? StartDate, DateOnly? EndDate, Guid ManagerId, Guid? Id = null);

public record UpdateProjectRequest(string Name, string? Description, string? Client,
    string? Address, DateOnly? StartDate, DateOnly? EndDate,
    ProjectStatus Status, Guid ManagerId,
    bool IsMarkedForDeletion = false, bool IsArchived = false);

public record ProjectResponse(Guid Id, string Name, string? Description, string? Client,
    string? Address, DateOnly? StartDate, DateOnly? EndDate, string Status,
    Guid ManagerId, string ManagerName,
    int TotalTasks, int CompletedTasks, int InProgressTasks, int OverdueTasks,
    DateTime CreatedAt, DateTime UpdatedAt,
    bool IsMarkedForDeletion = false, bool IsArchived = false);

public record ProjectListResponse(Guid Id, string Name, string? Client,
    DateOnly? StartDate, DateOnly? EndDate, string Status, string ManagerName,
    Guid ManagerId, string? Description, string? Address,
    DateTime CreatedAt, DateTime UpdatedAt,
    bool IsMarkedForDeletion, bool IsArchived);

// ── Tasks ─────────────────────────────────────────────────────────────────────
public record CreateTaskRequest(Guid ProjectId, string Name, string? Description,
    Guid? AssignedUserId, TaskPriority Priority, DateOnly? DueDate, Guid? Id = null);

public record UpdateTaskRequest(string Name, string? Description,
    Guid? AssignedUserId, TaskPriority Priority, DateOnly? DueDate, TaskStatus Status,
    bool IsMarkedForDeletion = false, bool IsArchived = false);

public record TaskListResponse(Guid Id, Guid ProjectId, string ProjectName,
    string Name, string? AssignedUserName, string Priority, DateOnly? DueDate,
    string Status, int TotalStages, int CompletedStages, int ProgressPercent, bool IsOverdue,
    Guid? AssignedUserId = null, bool IsMarkedForDeletion = false, bool IsArchived = false);

public record TaskResponse(Guid Id, Guid ProjectId, string ProjectName,
    string Name, string? Description, Guid? AssignedUserId, string? AssignedUserName,
    string Priority, DateOnly? DueDate, string Status,
    int TotalStages, int CompletedStages, int ProgressPercent, bool IsOverdue,
    DateTime CreatedAt, DateTime UpdatedAt, List<StageResponse>? Stages,
    bool IsMarkedForDeletion = false, bool IsArchived = false,
    List<Guid>? AssigneeUserIds = null);

// ── Stages ────────────────────────────────────────────────────────────────────
public record CreateStageRequest(Guid TaskId, string Name, string? Description,
    Guid? AssignedUserId, DateOnly? DueDate = null, Guid? Id = null,
    Guid? ServiceTemplateId = null, decimal WorkQuantity = 0, decimal? WorkPricePerUnit = null,
    List<StageServiceItemRequest>? ServiceItems = null);

public record UpdateStageRequest(string Name, string? Description,
    Guid? AssignedUserId, StageStatus Status, DateOnly? DueDate = null,
    bool IsMarkedForDeletion = false, bool IsArchived = false,
    Guid? ServiceTemplateId = null, decimal WorkQuantity = 0, decimal WorkPricePerUnit = 0,
    List<StageServiceItemRequest>? ServiceItems = null);

public record StageServiceItemRequest(Guid ServiceTemplateId, decimal Quantity, decimal? PricePerUnit = null);

public record StageResponse(Guid Id, Guid TaskId, string Name, string? Description,
    Guid? ServiceTemplateId, string? ServiceName, string? ServiceDescription, string? WorkUnit,
    decimal WorkQuantity, decimal WorkPricePerUnit, decimal WorkTotal, List<StageServiceResponse> Services, decimal MaterialTotal, decimal StageTotal,
    Guid? AssignedUserId, string? AssignedUserName, string Status,
    DateOnly? DueDate,
    List<StageMaterialResponse> Materials, List<FileDto> Files,
    DateTime CreatedAt, DateTime UpdatedAt,
    bool IsMarkedForDeletion = false, bool IsArchived = false,
    List<Guid>? AssigneeUserIds = null);

// ── Materials & inventory ────────────────────────────────────────────────────
public record MaterialCategoryResponse(Guid Id, string Name);
public record EquipmentCategoryResponse(Guid Id, string Name);
public record CreateMaterialCategoryRequest(string Name, Guid? Id = null);
public record CreateEquipmentCategoryRequest(string Name, Guid? Id = null);

public record CreateMaterialRequest(
    string Name,
    string? Unit,
    string? Description,
    Guid? Id = null,
    decimal InitialQuantity = 0,
    Guid? CategoryId = null,
    string? ImagePath = null,
    decimal? Cost = null,
    string? InventoryNumber = null);

public record UpdateMaterialRequest(
    string Name,
    string? Unit,
    string? Description,
    Guid? CategoryId = null,
    string? ImagePath = null,
    decimal? Cost = null,
    string? InventoryNumber = null,
    bool IsWrittenOff = false,
    DateTime? WrittenOffAt = null,
    string? WrittenOffComment = null,
    bool IsArchived = false);

public record MaterialResponse(
    Guid Id,
    string Name,
    string? Unit,
    string? Description,
    decimal Quantity,
    decimal? Cost,
    string? InventoryNumber,
    Guid? CategoryId,
    string? CategoryName,
    string? ImagePath,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsWrittenOff = false,
    DateTime? WrittenOffAt = null,
    string? WrittenOffComment = null,
    bool IsArchived = false);

public record MaterialStockMovementResponse(
    Guid Id,
    Guid MaterialId,
    DateTime OccurredAt,
    decimal Delta,
    decimal QuantityAfter,
    string OperationType,
    string? Comment,
    Guid? UserId,
    Guid? ProjectId,
    Guid? TaskId);

public enum MaterialStockOperationType
{
    Purchase = 0,
    Consumption = 1,
    Adjustment = 2,
    WriteOff = 3,
    ReturnToStock = 4
}

public record RecordMaterialStockRequest(
    decimal Delta,
    MaterialStockOperationType OperationType,
    string? Comment,
    Guid? ProjectId,
    Guid? TaskId);

public record EquipmentResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    string? ImagePath,
    string Status,
    string Condition,
    string? InventoryNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CheckedOutProjectId,
    Guid? CheckedOutTaskId,
    bool IsWrittenOff = false,
    DateTime? WrittenOffAt = null,
    string? WrittenOffComment = null,
    bool IsArchived = false);

public record EquipmentHistoryEntryResponse(
    Guid Id,
    Guid EquipmentId,
    DateTime OccurredAt,
    string EventType,
    string? PreviousStatus,
    string? NewStatus,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? UserId,
    string? Comment);

public enum EquipmentStatus
{
    Available = 0,
    InUse = 1,
    Retired = 2,
    Unavailable = 3
}

public enum EquipmentCondition
{
    Good = 0,
    NeedsMaintenance = 1,
    Faulty = 2
}

public enum EquipmentHistoryEventType
{
    CheckedOut = 0,
    Returned = 1,
    StatusChanged = 2,
    Note = 3,
    WrittenOff = 4
}

public record CreateEquipmentRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    string? ImagePath,
    string? InventoryNumber,
    EquipmentCondition Condition = EquipmentCondition.Good,
    Guid? Id = null);

public record UpdateEquipmentRequest(
    string Name,
    string? Description,
    Guid? CategoryId,
    string? ImagePath,
    string? InventoryNumber,
    EquipmentCondition Condition = EquipmentCondition.Good,
    EquipmentStatus? Status = null,
    bool IsWrittenOff = false,
    DateTime? WrittenOffAt = null,
    string? WrittenOffComment = null,
    bool IsArchived = false);

public record RecordEquipmentEventRequest(
    EquipmentHistoryEventType EventType,
    EquipmentStatus? NewStatus,
    Guid? ProjectId,
    Guid? TaskId,
    string? Comment);

public record StageMaterialResponse(Guid Id, Guid MaterialId, string MaterialName,
    string? Unit, decimal Quantity, decimal PricePerUnit, decimal Total);
public record StageServiceResponse(Guid Id, Guid ServiceTemplateId, string ServiceName, string? ServiceDescription,
    string? Unit, decimal Quantity, decimal PricePerUnit, decimal Total);
public record AddStageMaterialRequest(Guid MaterialId, decimal Quantity, decimal? PricePerUnit = null);

// ── Files ─────────────────────────────────────────────────────────────────────
public record FileDto(Guid Id, string FileName, string FileType, long FileSize,
    Guid UploadedById, string UploadedByName,
    Guid? ProjectId, Guid? TaskId, Guid? StageId, DateTime CreatedAt);

// ── Users ─────────────────────────────────────────────────────────────────────
public record UserResponse(Guid Id, string FirstName, string LastName, string Username, string? Email, string Role, Guid RoleId, DateTime CreatedAt, byte[]? AvatarData = null, string? SubRole = null, string? AdditionalSubRoles = null, DateOnly? BirthDate = null, string? HomeAddress = null, bool IsBlocked = false, DateTime? BlockedAt = null, string? BlockedReason = null);

public record UploadAvatarRequest(byte[] AvatarData);

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Username,
    string? Email,
    string Password,
    Guid RoleId,
    Guid? Id = null,
    byte[]? AvatarData = null,
    string? SubRole = null,
    string? AdditionalSubRoles = null,
    DateOnly? BirthDate = null,
    string? HomeAddress = null);

public record UpdateUserRequest(string FirstName, string LastName, string Username, string? Email, Guid RoleId, string? NewPassword = null, string? SubRole = null, string? AdditionalSubRoles = null, DateOnly? BirthDate = null, string? HomeAddress = null, bool? IsBlocked = null, string? BlockedReason = null);

// ── Sync: сообщения, активность, соисполнители ─────────────────────────────
public record AssigneeSyncItemDto(Guid Id, Guid UserId);

public record ReplaceTaskAssigneesRequest(List<AssigneeSyncItemDto> Assignees);

public record ReplaceStageAssigneesRequest(List<AssigneeSyncItemDto> Assignees);

public record DiscussionMessageResponse(
    Guid Id, Guid? TaskId, Guid? ProjectId, Guid UserId,
    string UserName, string UserInitials, string UserColor, string UserRole,
    string Text, DateTime CreatedAt);

public record CreateDiscussionMessageRequest(Guid? Id, Guid? TaskId, Guid? ProjectId, string Text, DateTime? CreatedAt = null);

public record SyncedActivityLogResponse(
    Guid Id, Guid? UserId, string? ActorRole, string UserName, string UserInitials, string UserColor,
    string? ActionType, string ActionText, string EntityType, Guid EntityId, DateTime CreatedAt);

public record CreateSyncedActivityLogRequest(
    Guid Id, Guid? UserId, string? ActorRole, string UserName, string UserInitials, string UserColor,
    string? ActionType, string ActionText, string EntityType, Guid EntityId, DateTime CreatedAt);
