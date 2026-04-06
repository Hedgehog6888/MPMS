namespace MPMS.API.DTOs;

public record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Username,
    string? Email,
    string Role,
    Guid RoleId,
    DateTime CreatedAt,
    byte[]? AvatarData = null,
    string? SubRole = null,
    string? AdditionalSubRoles = null,
    DateOnly? BirthDate = null,
    string? HomeAddress = null,
    bool IsBlocked = false,
    DateTime? BlockedAt = null,
    string? BlockedReason = null
);

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
    string? HomeAddress = null
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Username,
    string? Email,
    Guid RoleId,
    string? NewPassword = null,
    string? SubRole = null,
    string? AdditionalSubRoles = null,
    DateOnly? BirthDate = null,
    string? HomeAddress = null,
    bool? IsBlocked = null,
    string? BlockedReason = null
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
