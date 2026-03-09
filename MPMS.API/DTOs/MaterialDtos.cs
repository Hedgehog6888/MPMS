using System.ComponentModel.DataAnnotations;

namespace MPMS.API.DTOs;

public record CreateMaterialRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(50)] string? Unit,
    string? Description
);

public record UpdateMaterialRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(50)] string? Unit,
    string? Description
);

public record MaterialResponse(
    Guid Id,
    string Name,
    string? Unit,
    string? Description,
    DateTime CreatedAt
);
