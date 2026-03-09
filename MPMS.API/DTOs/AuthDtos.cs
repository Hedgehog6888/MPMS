using System.ComponentModel.DataAnnotations;

namespace MPMS.API.DTOs;

public record LoginRequest(
    [Required] string Username,
    [Required] string Password
);

public record RegisterRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(50)] string Username,
    [MaxLength(255)] string? Email,
    [Required, MinLength(6)] string Password,
    Guid RoleId
);

public record AuthResponse(
    Guid UserId,
    string Name,
    string Username,
    string Role,
    string Token,
    DateTime ExpiresAt
);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(6)] string NewPassword
);
