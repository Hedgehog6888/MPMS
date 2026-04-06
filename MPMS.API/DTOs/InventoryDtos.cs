using System.ComponentModel.DataAnnotations;
using MPMS.API.Models;

namespace MPMS.API.DTOs;

// ── Categories ───────────────────────────────────────────────────────────────
public record MaterialCategoryResponse(Guid Id, string Name);

public record CreateMaterialCategoryRequest([Required, MaxLength(100)] string Name, Guid? Id = null);

public record EquipmentCategoryResponse(Guid Id, string Name);

public record CreateEquipmentCategoryRequest([Required, MaxLength(100)] string Name, Guid? Id = null);

// ── Material stock ───────────────────────────────────────────────────────────
public record MaterialStockMovementResponse(
    Guid Id,
    Guid MaterialId,
    DateTime OccurredAt,
    decimal Delta,
    decimal QuantityAfter,
    string OperationType,
    string? Comment,
    Guid? UserId,
    Guid? ProjectId,
    Guid? TaskId);

public record RecordMaterialStockRequest(
    decimal Delta,
    MaterialStockOperationType OperationType,
    string? Comment,
    Guid? ProjectId,
    Guid? TaskId);

// ── Equipment ────────────────────────────────────────────────────────────────
public record EquipmentResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    string? ImagePath,
    string Status,
    string Condition,
    string? InventoryNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? CheckedOutProjectId,
    Guid? CheckedOutTaskId);

public record CreateEquipmentRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? CategoryId,
    string? ImagePath,
    string? InventoryNumber,
    EquipmentCondition Condition = EquipmentCondition.Good,
    Guid? Id = null);

public record UpdateEquipmentRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? CategoryId,
    string? ImagePath,
    string? InventoryNumber,
    EquipmentCondition Condition = EquipmentCondition.Good);

public record EquipmentHistoryEntryResponse(
    Guid Id,
    Guid EquipmentId,
    DateTime OccurredAt,
    string EventType,
    string? PreviousStatus,
    string? NewStatus,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? UserId,
    string? Comment);

public record RecordEquipmentEventRequest(
    EquipmentHistoryEventType EventType,
    EquipmentStatus? NewStatus,
    Guid? ProjectId,
    Guid? TaskId,
    string? Comment);
