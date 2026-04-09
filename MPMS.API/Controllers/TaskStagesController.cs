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

        ServiceTemplate? serviceTemplate = null;
        if (request.ServiceTemplateId.HasValue)
        {
            serviceTemplate = await _db.ServiceTemplates
                .FirstOrDefaultAsync(s => s.Id == request.ServiceTemplateId.Value && s.IsActive);
            if (serviceTemplate is null)
                return BadRequest(new { message = "Услуга не найдена" });
        }

        var workPricePerUnit = request.WorkPricePerUnit ?? serviceTemplate?.BasePrice ?? 0m;
        var workQuantity = request.WorkQuantity;

        var stage = new TaskStage
        {
            Id = id,
            TaskId = request.TaskId,
            Name = request.Name,
            Description = request.Description,
            ServiceTemplateId = request.ServiceTemplateId,
            ServiceNameSnapshot = serviceTemplate?.Name,
            ServiceDescriptionSnapshot = serviceTemplate?.Description,
            WorkUnitSnapshot = serviceTemplate?.Unit,
            WorkQuantity = workQuantity,
            WorkPricePerUnit = workPricePerUnit,
            AssignedUserId = request.AssignedUserId,
            DueDate = request.DueDate,
            Status = StageStatus.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TaskStages.Add(stage);

        var serviceItems = request.ServiceItems ?? [];
        if (serviceItems.Count > 0)
        {
            var ids = serviceItems.Select(x => x.ServiceTemplateId).Distinct().ToList();
            var templates = await _db.ServiceTemplates
                .Where(s => ids.Contains(s.Id) && s.IsActive)
                .ToDictionaryAsync(s => s.Id);
            if (templates.Count != ids.Count)
                return BadRequest(new { message = "Одна или несколько услуг не найдены" });

            foreach (var item in serviceItems)
            {
                var tpl = templates[item.ServiceTemplateId];
                _db.StageServices.Add(new StageService
                {
                    StageId = stage.Id,
                    ServiceTemplateId = tpl.Id,
                    ServiceNameSnapshot = tpl.Name,
                    ServiceDescriptionSnapshot = tpl.Description,
                    UnitSnapshot = tpl.Unit,
                    Quantity = item.Quantity,
                    PricePerUnit = item.PricePerUnit ?? tpl.BasePrice
                });
            }
        }

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
        stage.ServiceTemplateId = request.ServiceTemplateId;
        if (request.ServiceTemplateId.HasValue)
        {
            var serviceTemplate = await _db.ServiceTemplates.FirstOrDefaultAsync(s => s.Id == request.ServiceTemplateId.Value);
            if (serviceTemplate is null)
                return BadRequest(new { message = "Услуга не найдена" });

            stage.ServiceNameSnapshot = serviceTemplate.Name;
            stage.ServiceDescriptionSnapshot = serviceTemplate.Description;
            stage.WorkUnitSnapshot = serviceTemplate.Unit;
        }
        else
        {
            stage.ServiceNameSnapshot = null;
            stage.ServiceDescriptionSnapshot = null;
            stage.WorkUnitSnapshot = null;
        }
        stage.WorkQuantity = request.WorkQuantity;
        stage.WorkPricePerUnit = request.WorkPricePerUnit;
        var serviceItems = request.ServiceItems ?? [];
        if (serviceItems.Count > 0)
        {
            var ids = serviceItems.Select(x => x.ServiceTemplateId).Distinct().ToList();
            var templates = await _db.ServiceTemplates
                .Where(s => ids.Contains(s.Id) && s.IsActive)
                .ToDictionaryAsync(s => s.Id);
            if (templates.Count != ids.Count)
                return BadRequest(new { message = "Одна или несколько услуг не найдены" });

            var existingServices = await _db.StageServices.Where(x => x.StageId == id).ToListAsync();
            _db.StageServices.RemoveRange(existingServices);
            foreach (var item in serviceItems)
            {
                var tpl = templates[item.ServiceTemplateId];
                _db.StageServices.Add(new StageService
                {
                    Id = Guid.NewGuid(),
                    StageId = id,
                    ServiceTemplateId = tpl.Id,
                    ServiceNameSnapshot = tpl.Name,
                    ServiceDescriptionSnapshot = tpl.Description,
                    UnitSnapshot = tpl.Unit,
                    Quantity = item.Quantity,
                    PricePerUnit = item.PricePerUnit ?? tpl.BasePrice
                });
            }
        }
        else if (!request.ServiceTemplateId.HasValue)
        {
            var existingServices = await _db.StageServices.Where(x => x.StageId == id).ToListAsync();
            _db.StageServices.RemoveRange(existingServices);
        }
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
        var stage = await _db.TaskStages
            .Where(s => s.Id == id)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync();
        if (stage is null) return NotFound(new { message = "Этап не найден" });

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
                ,
                PricePerUnit = material.Cost ?? 0m
            };
            _db.StageMaterials.Add(existing);
        }

        await _db.SaveChangesAsync();

        await _log.LogAsync(
            CurrentUserId(),
            ActivityActionType.Updated,
            ActivityEntityType.TaskStage,
            stage.Id,
            $"В этап «{stage.Name}» добавлен материал «{material.Name}» в количестве {request.Quantity:g} {(material.Unit ?? "").Trim()}".Trim());

        return Ok(new StageMaterialResponse(
            existing.Id,
            material.Id,
            material.Name,
            material.Unit,
            existing.Quantity,
            existing.PricePerUnit,
            existing.Quantity * existing.PricePerUnit));
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
            .Include(s => s.StageServices)
            .Include(s => s.StageMaterials)
                .ThenInclude(sm => sm.Material)
            .Include(s => s.Files)
                .ThenInclude(f => f.UploadedBy)
            .FirstOrDefaultAsync(s => s.Id == id);

    private static TaskStageResponse MapToResponse(TaskStage s) =>
        new(
            s.Id, s.TaskId, s.Name, s.Description,
            s.ServiceTemplateId,
            s.ServiceNameSnapshot,
            s.ServiceDescriptionSnapshot,
            s.WorkUnitSnapshot,
            s.WorkQuantity,
            s.WorkPricePerUnit,
            (s.StageServices.Count > 0
                ? s.StageServices.Sum(ss => ss.Quantity * ss.PricePerUnit)
                : s.WorkQuantity * s.WorkPricePerUnit),
            s.StageServices.Select(ss => new StageServiceResponse(
                ss.Id,
                ss.ServiceTemplateId,
                ss.ServiceNameSnapshot,
                ss.ServiceDescriptionSnapshot,
                ss.UnitSnapshot,
                ss.Quantity,
                ss.PricePerUnit,
                ss.Quantity * ss.PricePerUnit)).ToList(),
            s.StageMaterials.Sum(sm => sm.Quantity * sm.PricePerUnit),
            (s.StageServices.Count > 0
                ? s.StageServices.Sum(ss => ss.Quantity * ss.PricePerUnit)
                : s.WorkQuantity * s.WorkPricePerUnit) + s.StageMaterials.Sum(sm => sm.Quantity * sm.PricePerUnit),
            s.AssignedUserId, s.AssignedUser?.Name,
            s.Status.ToString(),
            s.DueDate,
            s.StageMaterials.Select(sm => new StageMaterialResponse(
                sm.Id,
                sm.MaterialId,
                sm.Material.Name,
                sm.Material.Unit,
                sm.Quantity,
                sm.PricePerUnit,
                sm.Quantity * sm.PricePerUnit)).ToList(),
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
