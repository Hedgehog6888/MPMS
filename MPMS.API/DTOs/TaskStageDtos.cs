using System.ComponentModel.DataAnnotations;
using MPMS.API.Models;

namespace MPMS.API.DTOs;

public record CreateStageRequest(
    Guid TaskId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    DateOnly? DueDate = null,
    Guid? Id = null
);

public record UpdateStageRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    StageStatus Status,
    DateOnly? DueDate = null
);

public record TaskStageResponse(
    Guid Id,
    Guid TaskId,
    string Name,
    string? Description,
    Guid? AssignedUserId,
    string? AssignedUserName,
    string Status,
    DateOnly? DueDate,
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
