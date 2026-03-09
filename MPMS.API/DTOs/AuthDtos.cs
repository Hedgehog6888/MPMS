using System.ComponentModel.DataAnnotations;

namespace MPMS.API.DTOs;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record RegisterRequest(
    [Required, MaxLength(100)] string Name,
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(6)] string Password,
    Guid RoleId
);

public record AuthResponse(
    Guid UserId,
    string Name,
    string Email,
    string Role,
    string Token,
    DateTime ExpiresAt
);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, MinLength(6)] string NewPassword
);
