namespace MPMS.API.DTOs;

public record UserResponse(
    Guid Id,
    string Name,
    string Username,
    string? Email,
    string Role,
    DateTime CreatedAt
);

public record ActivityLogResponse(
    Guid Id,
    Guid UserId,
    string UserName,
    string ActionType,
    string EntityType,
    Guid EntityId,
    string? Description,
    DateTime CreatedAt
);
