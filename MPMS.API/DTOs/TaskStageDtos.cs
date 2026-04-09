using System.ComponentModel.DataAnnotations;
using MPMS.API.Models;

namespace MPMS.API.DTOs;

public record CreateStageRequest(
    Guid TaskId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    DateOnly? DueDate = null,
    Guid? Id = null,
    Guid? ServiceTemplateId = null,
    decimal WorkQuantity = 0,
    decimal? WorkPricePerUnit = null,
    List<StageServiceItemRequest>? ServiceItems = null
);

public record UpdateStageRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    StageStatus Status,
    DateOnly? DueDate = null,
    bool IsMarkedForDeletion = false,
    bool IsArchived = false,
    Guid? ServiceTemplateId = null,
    decimal WorkQuantity = 0,
    decimal WorkPricePerUnit = 0,
    List<StageServiceItemRequest>? ServiceItems = null
);

public record StageServiceItemRequest(
    Guid ServiceTemplateId,
    decimal Quantity,
    decimal? PricePerUnit = null
);

public record TaskStageResponse(
    Guid Id,
    Guid TaskId,
    string Name,
    string? Description,
    Guid? ServiceTemplateId,
    string? ServiceName,
    string? ServiceDescription,
    string? WorkUnit,
    decimal WorkQuantity,
    decimal WorkPricePerUnit,
    decimal WorkTotal,
    List<StageServiceResponse> Services,
    decimal MaterialTotal,
    decimal StageTotal,
    Guid? AssignedUserId,
    string? AssignedUserName,
    string Status,
    DateOnly? DueDate,
    List<StageMaterialResponse> Materials,
    List<FileResponse> Files,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsMarkedForDeletion = false,
    bool IsArchived = false,
    List<Guid>? AssigneeUserIds = null
);

public record AddStageMaterialRequest(
    Guid MaterialId,
    decimal Quantity,
    decimal? PricePerUnit = null
);

public record StageMaterialResponse(
    Guid Id,
    Guid MaterialId,
    string MaterialName,
    string? Unit,
    decimal Quantity,
    decimal PricePerUnit,
    decimal Total
);

public record StageServiceResponse(
    Guid Id,
    Guid ServiceTemplateId,
    string ServiceName,
    string? ServiceDescription,
    string? Unit,
    decimal Quantity,
    decimal PricePerUnit,
    decimal Total
);
