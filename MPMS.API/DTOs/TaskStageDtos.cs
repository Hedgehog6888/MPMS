using System.ComponentModel.DataAnnotations;
using MPMS.API.Models;

namespace MPMS.API.DTOs;

public record CreateStageRequest(
    Guid TaskId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    Guid? Id = null
);

public record UpdateStageRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    StageStatus Status
);

public record TaskStageResponse(
    Guid Id,
    Guid TaskId,
    string Name,
    string? Description,
    Guid? AssignedUserId,
    string? AssignedUserName,
    string Status,
    List<StageMaterialResponse> Materials,
    List<FileResponse> Files,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record AddStageMaterialRequest(
    Guid MaterialId,
    decimal Quantity
);

public record StageMaterialResponse(
    Guid Id,
    Guid MaterialId,
    string MaterialName,
    string? Unit,
    decimal Quantity
);
