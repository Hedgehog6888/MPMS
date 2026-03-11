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
}
public record RegisterRequest(string Name, string Username, string? Email, string Password, Guid RoleId);
public record AuthResponse(Guid UserId, string Name, string Username, string Role,
    string Token, DateTime ExpiresAt);
public record RoleDto(Guid Id, string Name, string? Description);

// ── Projects ──────────────────────────────────────────────────────────────────
public record CreateProjectRequest(string Name, string? Description, string? Client,
    string? Address, DateOnly? StartDate, DateOnly? EndDate, Guid ManagerId, Guid? Id = null);

public record UpdateProjectRequest(string Name, string? Description, string? Client,
    string? Address, DateOnly? StartDate, DateOnly? EndDate,
    ProjectStatus Status, Guid ManagerId);

public record ProjectResponse(Guid Id, string Name, string? Description, string? Client,
    string? Address, DateOnly? StartDate, DateOnly? EndDate, string Status,
    Guid ManagerId, string ManagerName,
    int TotalTasks, int CompletedTasks, int InProgressTasks, int OverdueTasks,
    DateTime CreatedAt, DateTime UpdatedAt);

public record ProjectListResponse(Guid Id, string Name, string? Client,
    DateOnly? StartDate, DateOnly? EndDate, string Status, string ManagerName);

// ── Tasks ─────────────────────────────────────────────────────────────────────
public record CreateTaskRequest(Guid ProjectId, string Name, string? Description,
    Guid? AssignedUserId, TaskPriority Priority, DateOnly? DueDate, Guid? Id = null);

public record UpdateTaskRequest(string Name, string? Description,
    Guid? AssignedUserId, TaskPriority Priority, DateOnly? DueDate, TaskStatus Status);

public record TaskListResponse(Guid Id, Guid ProjectId, string ProjectName,
    string Name, string? AssignedUserName, string Priority, DateOnly? DueDate,
    string Status, int TotalStages, int CompletedStages, int ProgressPercent, bool IsOverdue);

public record TaskResponse(Guid Id, Guid ProjectId, string ProjectName,
    string Name, string? Description, Guid? AssignedUserId, string? AssignedUserName,
    string Priority, DateOnly? DueDate, string Status,
    int TotalStages, int CompletedStages, int ProgressPercent, bool IsOverdue,
    DateTime CreatedAt, DateTime UpdatedAt, List<StageResponse>? Stages);

// ── Stages ────────────────────────────────────────────────────────────────────
public record CreateStageRequest(Guid TaskId, string Name, string? Description,
    Guid? AssignedUserId, Guid? Id = null);

public record UpdateStageRequest(string Name, string? Description,
    Guid? AssignedUserId, StageStatus Status);

public record StageResponse(Guid Id, Guid TaskId, string Name, string? Description,
    Guid? AssignedUserId, string? AssignedUserName, string Status,
    List<StageMaterialResponse> Materials, List<FileDto> Files,
    DateTime CreatedAt, DateTime UpdatedAt);

// ── Materials ─────────────────────────────────────────────────────────────────
public record CreateMaterialRequest(string Name, string? Unit, string? Description, Guid? Id = null);
public record UpdateMaterialRequest(string Name, string? Unit, string? Description);
public record MaterialResponse(Guid Id, string Name, string? Unit,
    string? Description, DateTime CreatedAt);

public record StageMaterialResponse(Guid Id, Guid MaterialId, string MaterialName,
    string? Unit, decimal Quantity);
public record AddStageMaterialRequest(Guid MaterialId, decimal Quantity);

// ── Files ─────────────────────────────────────────────────────────────────────
public record FileDto(Guid Id, string FileName, string FileType, long FileSize,
    Guid UploadedById, string UploadedByName,
    Guid? ProjectId, Guid? TaskId, Guid? StageId, DateTime CreatedAt);

// ── Users ─────────────────────────────────────────────────────────────────────
public record UserResponse(Guid Id, string Name, string Username, string? Email, string Role, DateTime CreatedAt);
