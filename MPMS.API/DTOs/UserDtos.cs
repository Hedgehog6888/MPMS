namespace MPMS.API.DTOs;

public record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Username,
    string? Email,
    string Role,
    DateTime CreatedAt,
    byte[]? AvatarData = null,
    string? SubRole = null,
    string? AdditionalSubRoles = null
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
    string? AdditionalSubRoles = null
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Username,
    string? Email,
    Guid RoleId,
    string? NewPassword = null,
    string? SubRole = null,
    string? AdditionalSubRoles = null
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
