using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public EquipmentController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static EquipmentResponse ToDto(Equipment e) => new(
        e.Id, e.Name, e.Description, e.CategoryId, e.Category?.Name, e.ImagePath,
        e.Status.ToString(), e.InventoryNumber, e.CreatedAt, e.UpdatedAt,
        e.CheckedOutProjectId, e.CheckedOutTaskId);

    private static EquipmentHistoryEntryResponse HistoryToDto(EquipmentHistoryEntry x) => new(
        x.Id, x.EquipmentId, x.OccurredAt, x.EventType.ToString(),
        x.PreviousStatus?.ToString(), x.NewStatus?.ToString(),
        x.ProjectId, x.TaskId, x.UserId, x.Comment);

    [HttpGet]
    public async Task<ActionResult<List<EquipmentResponse>>> GetAll([FromQuery] string? search)
    {
        var q = _db.Equipments.Include(e => e.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(e => e.Name.Contains(search));
        var list = await q.OrderBy(e => e.Name).ToListAsync();
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EquipmentResponse>> GetById(Guid id)
    {
        var e = await _db.Equipments.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        return Ok(ToDto(e));
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentResponse>> Create([FromBody] CreateEquipmentRequest request)
    {
        if (request.CategoryId.HasValue &&
            !await _db.EquipmentCategories.AnyAsync(c => c.Id == request.CategoryId.Value))
            return BadRequest(new { message = "Категория оборудования не найдена" });

        var now = DateTime.UtcNow;
        var entity = new Equipment
        {
            Id = request.Id ?? Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            CategoryId = request.CategoryId,
            ImagePath = request.ImagePath,
            InventoryNumber = request.InventoryNumber,
            Status = EquipmentStatus.Available,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Equipments.Add(entity);
        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(x => x.Category).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, ToDto(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EquipmentResponse>> Update(Guid id, [FromBody] UpdateEquipmentRequest request)
    {
        var entity = await _db.Equipments.FindAsync(id);
        if (entity is null) return NotFound();

        if (request.CategoryId.HasValue &&
            !await _db.EquipmentCategories.AnyAsync(c => c.Id == request.CategoryId.Value))
            return BadRequest(new { message = "Категория оборудования не найдена" });

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.CategoryId = request.CategoryId;
        entity.ImagePath = request.ImagePath;
        entity.InventoryNumber = request.InventoryNumber;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(x => x.Category).LoadAsync();
        return Ok(ToDto(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entity = await _db.Equipments.FindAsync(id);
        if (entity is null) return NotFound();
        _db.Equipments.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<List<EquipmentHistoryEntryResponse>>> GetHistory(Guid id)
    {
        if (!await _db.Equipments.AnyAsync(e => e.Id == id)) return NotFound();
        var list = await _db.EquipmentHistoryEntries
            .Where(h => h.EquipmentId == id)
            .OrderByDescending(h => h.OccurredAt)
            .ToListAsync();
        return Ok(list.Select(HistoryToDto).ToList());
    }

    [HttpPost("{id:guid}/history")]
    public async Task<ActionResult<EquipmentHistoryEntryResponse>> AppendHistory(Guid id,
        [FromBody] RecordEquipmentEventRequest request)
    {
        var eq = await _db.Equipments.FirstOrDefaultAsync(e => e.Id == id);
        if (eq is null) return NotFound();

        var userId = CurrentUserId();
        var now = DateTime.UtcNow;
        var prevStatus = eq.Status;

        Guid? histProject = request.ProjectId;
        Guid? histTask = request.TaskId;

        switch (request.EventType)
        {
            case EquipmentHistoryEventType.CheckedOut:
                if (eq.Status != EquipmentStatus.Available)
                    return BadRequest(new { message = "Выдать можно только свободное оборудование" });
                if (request.ProjectId.HasValue &&
                    !await _db.Projects.AnyAsync(p => p.Id == request.ProjectId.Value))
                    return BadRequest(new { message = "Проект не найден" });
                if (request.TaskId.HasValue &&
                    !await _db.Tasks.AnyAsync(t => t.Id == request.TaskId.Value))
                    return BadRequest(new { message = "Задача не найдена" });
                eq.Status = EquipmentStatus.CheckedOut;
                eq.CheckedOutProjectId = request.ProjectId;
                eq.CheckedOutTaskId = request.TaskId;
                break;

            case EquipmentHistoryEventType.Returned:
                if (eq.Status != EquipmentStatus.CheckedOut)
                    return BadRequest(new { message = "Возврат только для выданного оборудования" });
                histProject = eq.CheckedOutProjectId;
                histTask = eq.CheckedOutTaskId;
                eq.Status = EquipmentStatus.Available;
                eq.CheckedOutProjectId = null;
                eq.CheckedOutTaskId = null;
                break;

            case EquipmentHistoryEventType.StatusChanged:
                if (request.NewStatus is null)
                    return BadRequest(new { message = "Укажите новый статус" });
                eq.Status = request.NewStatus.Value;
                if (eq.Status != EquipmentStatus.CheckedOut)
                {
                    eq.CheckedOutProjectId = null;
                    eq.CheckedOutTaskId = null;
                }
                break;

            case EquipmentHistoryEventType.Note:
                break;

            default:
                return BadRequest();
        }

        eq.UpdatedAt = now;

        var entry = new EquipmentHistoryEntry
        {
            EquipmentId = id,
            OccurredAt = now,
            EventType = request.EventType,
            PreviousStatus = prevStatus,
            NewStatus = eq.Status,
            ProjectId = histProject,
            TaskId = histTask,
            UserId = userId,
            Comment = request.Comment
        };

        _db.EquipmentHistoryEntries.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(HistoryToDto(entry));
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
