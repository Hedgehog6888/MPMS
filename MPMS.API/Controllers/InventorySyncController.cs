using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Bulk read endpoints for offline sync.</summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventorySyncController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public InventorySyncController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static MaterialStockMovementResponse StockToDto(MaterialStockMovement x) => new(
        x.Id, x.MaterialId, x.OccurredAt, x.Delta, x.QuantityAfter,
        x.OperationType.ToString(), x.Comment, x.UserId, x.ProjectId, x.TaskId);

    private static EquipmentResponse EqToDto(Equipment e) => new(
        e.Id, e.Name, e.Description, e.CategoryId, e.Category?.Name, e.ImagePath,
        e.Status.ToString(), e.Condition.ToString(), e.InventoryNumber, e.CreatedAt, e.UpdatedAt,
        e.CheckedOutProjectId, e.CheckedOutTaskId, e.IsWrittenOff, e.WrittenOffAt, e.WrittenOffComment, e.IsArchived);

    private static EquipmentHistoryEntryResponse HistToDto(EquipmentHistoryEntry x) => new(
        x.Id, x.EquipmentId, x.OccurredAt, x.EventType.ToString(),
        x.PreviousStatus?.ToString(), x.NewStatus?.ToString(),
        x.ProjectId, x.TaskId, x.UserId, x.Comment);

    [HttpGet("material-stock-movements")]
    public async Task<ActionResult<List<MaterialStockMovementResponse>>> GetAllMaterialStockMovements()
    {
        var list = await _db.MaterialStockMovements
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();
        return Ok(list.Select(StockToDto).ToList());
    }

    [HttpGet("equipment")]
    public async Task<ActionResult<List<EquipmentResponse>>> GetAllEquipment()
    {
        var list = await _db.Equipments
            .Include(e => e.Category)
            .OrderBy(e => e.Name)
            .ToListAsync();
        return Ok(list.Select(EqToDto).ToList());
    }

    [HttpGet("equipment-history")]
    public async Task<ActionResult<List<EquipmentHistoryEntryResponse>>> GetAllEquipmentHistory()
    {
        var list = await _db.EquipmentHistoryEntries
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();
        return Ok(list.Select(HistToDto).ToList());
    }
}
