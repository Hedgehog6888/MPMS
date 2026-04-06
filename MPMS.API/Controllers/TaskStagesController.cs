using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;
using MPMS.API.Services;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TaskStagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IActivityLogService _log;

    public TaskStagesController(ApplicationDbContext db, IActivityLogService log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Get stage by ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskStageResponse>> GetById(Guid id)
    {
        var stage = await LoadStage(id);
        if (stage is null) return NotFound();
        return Ok(MapToResponse(stage));
    }

    /// <summary>Create a new stage</summary>
    [HttpPost]
    public async Task<ActionResult<TaskStageResponse>> Create([FromBody] CreateStageRequest request)
    {
        var id = request.Id ?? Guid.NewGuid();

        // Повтор POST с тем же Id (очередь синхронизации) — идемпотентно, без конфликта PK
        if (await LoadStage(id) is { } existingStage)
            return Ok(MapToResponse(existingStage));

        var taskExists = await _db.Tasks.AnyAsync(t => t.Id == request.TaskId);
        if (!taskExists) return BadRequest(new { message = "Задача не найдена" });

        if (request.AssignedUserId.HasValue)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == request.AssignedUserId.Value);
            if (!userExists) return BadRequest(new { message = "Исполнитель не найден" });
        }

        if (!DueDatePolicy.IsAllowed(request.DueDate))
            return BadRequest(new { message = DueDatePolicy.PastNotAllowedMessage });

        var stage = new TaskStage
        {
            Id = id,
            TaskId = request.TaskId,
            Name = request.Name,
            Description = request.Description,
            AssignedUserId = request.AssignedUserId,
            DueDate = request.DueDate,
            Status = StageStatus.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TaskStages.Add(stage);
        await _db.SaveChangesAsync();

        await _log.LogAsync(CurrentUserId(), ActivityActionType.Created,
            ActivityEntityType.TaskStage, stage.Id, $"Создан этап: {stage.Name}");

        var created = await LoadStage(stage.Id);
        return CreatedAtAction(nameof(GetById), new { id = stage.Id }, MapToResponse(created!));
    }

    /// <summary>Update stage</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskStageResponse>> Update(Guid id, [FromBody] UpdateStageRequest request)
    {
        var stage = await _db.TaskStages.FindAsync(id);
        if (stage is null) return NotFound();

        if (!DueDatePolicy.IsAllowed(request.DueDate))
            return BadRequest(new { message = DueDatePolicy.PastNotAllowedMessage });

        var oldStatus = stage.Status;

        stage.Name = request.Name;
        stage.Description = request.Description;
        stage.AssignedUserId = request.AssignedUserId;
        stage.Status = request.Status;
        stage.DueDate = request.DueDate;
        stage.IsMarkedForDeletion = request.IsMarkedForDeletion;
        stage.IsArchived = request.IsArchived;
        stage.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var actionType = oldStatus != request.Status
            ? ActivityActionType.StatusChanged
            : ActivityActionType.Updated;

        await _log.LogAsync(CurrentUserId(), actionType, ActivityEntityType.TaskStage, stage.Id,
            oldStatus != request.Status
                ? $"Статус этапа: {oldStatus} → {request.Status}"
                : $"Обновлён этап: {stage.Name}");

        var updated = await LoadStage(id);
        return Ok(MapToResponse(updated!));
    }

    /// <summary>Delete stage</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var stage = await _db.TaskStages.FindAsync(id);
        if (stage is null) return NotFound();

        _db.TaskStages.Remove(stage);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Add material to stage</summary>
    [HttpPost("{id:guid}/materials")]
    public async Task<ActionResult<StageMaterialResponse>> AddMaterial(
        Guid id, [FromBody] AddStageMaterialRequest request)
    {
        var stageExists = await _db.TaskStages.AnyAsync(s => s.Id == id);
        if (!stageExists) return NotFound(new { message = "Этап не найден" });

        var material = await _db.Materials.FindAsync(request.MaterialId);
        if (material is null) return BadRequest(new { message = "Материал не найден" });

        var existing = await _db.StageMaterials
            .FirstOrDefaultAsync(sm => sm.StageId == id && sm.MaterialId == request.MaterialId);

        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            existing = new StageMaterial
            {
                StageId = id,
                MaterialId = request.MaterialId,
                Quantity = request.Quantity
            };
            _db.StageMaterials.Add(existing);
        }

        await _db.SaveChangesAsync();

        return Ok(new StageMaterialResponse(
            existing.Id, material.Id, material.Name, material.Unit, existing.Quantity));
    }

    /// <summary>Remove material from stage</summary>
    [HttpDelete("{id:guid}/materials/{stageMaterialId:guid}")]
    public async Task<IActionResult> RemoveMaterial(Guid id, Guid stageMaterialId)
    {
        var sm = await _db.StageMaterials
            .FirstOrDefaultAsync(sm => sm.StageId == id && sm.Id == stageMaterialId);

        if (sm is null) return NotFound();

        _db.StageMaterials.Remove(sm);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Полная замена соисполнителей этапа.</summary>
    [HttpPut("{id:guid}/assignees")]
    public async Task<IActionResult> ReplaceAssignees(Guid id, [FromBody] ReplaceStageAssigneesRequest request)
    {
        var stage = await _db.TaskStages.FindAsync(id);
        if (stage is null) return NotFound();

        var items = request.Assignees ?? [];
        foreach (var uid in items.Select(a => a.UserId).Distinct())
        {
            if (!await _db.Users.AnyAsync(u => u.Id == uid))
                return BadRequest(new { message = "Пользователь не найден" });
        }

        var existing = await _db.StageAssignees.Where(x => x.StageId == id).ToListAsync();
        _db.StageAssignees.RemoveRange(existing);

        foreach (var a in items)
        {
            _db.StageAssignees.Add(new StageAssignee
            {
                Id = a.Id == Guid.Empty ? Guid.NewGuid() : a.Id,
                StageId = id,
                UserId = a.UserId
            });
        }

        stage.AssignedUserId = items.Count > 0 ? items[0].UserId : null;
        stage.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<TaskStage?> LoadStage(Guid id) =>
        await _db.TaskStages
            .Include(s => s.AssignedUser)
            .Include(s => s.StageAssignees)
            .Include(s => s.StageMaterials)
                .ThenInclude(sm => sm.Material)
            .Include(s => s.Files)
                .ThenInclude(f => f.UploadedBy)
            .FirstOrDefaultAsync(s => s.Id == id);

    private static TaskStageResponse MapToResponse(TaskStage s) =>
        new(s.Id, s.TaskId, s.Name, s.Description,
            s.AssignedUserId, s.AssignedUser?.Name,
            s.Status.ToString(),
            s.DueDate,
            s.StageMaterials.Select(sm => new StageMaterialResponse(
                sm.Id, sm.MaterialId, sm.Material.Name, sm.Material.Unit, sm.Quantity)).ToList(),
            s.Files.Select(f => new FileResponse(
                f.Id, f.FileName, f.FileType ?? "", f.FileSize,
                f.UploadedById, f.UploadedBy.Name,
                f.ProjectId, f.TaskId, f.StageId, f.CreatedAt)).ToList(),
            s.CreatedAt, s.UpdatedAt,
            s.IsMarkedForDeletion, s.IsArchived,
            s.StageAssignees.Select(x => x.UserId).ToList());

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
