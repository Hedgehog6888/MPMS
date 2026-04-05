using System.ComponentModel.DataAnnotations;

namespace MPMS.API.DTOs;

public record CreateMaterialRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(50)] string? Unit,
    string? Description,
    Guid? Id = null,
    decimal InitialQuantity = 0,
    Guid? CategoryId = null,
    string? ImagePath = null,
    decimal? Cost = null,
    [MaxLength(100)] string? InventoryNumber = null);

public record UpdateMaterialRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(50)] string? Unit,
    string? Description,
    Guid? CategoryId = null,
    string? ImagePath = null,
    decimal? Cost = null,
    [MaxLength(100)] string? InventoryNumber = null);

public record MaterialResponse(
    Guid Id,
    string Name,
    string? Unit,
    string? Description,
    decimal Quantity,
    decimal? Cost,
    string? InventoryNumber,
    Guid? CategoryId,
    string? CategoryName,
    string? ImagePath,
    DateTime CreatedAt,
    DateTime UpdatedAt);
