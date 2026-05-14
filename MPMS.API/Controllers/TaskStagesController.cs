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

    /// <summary>Получить этап по идентификатору.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskStageResponse>> GetById(Guid id)
    {
        var stage = await LoadStage(id);
        if (stage is null) return NotFound();
        return Ok(MapToResponse(stage));
    }

    /// <summary>Создать новый этап задачи.</summary>
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

        WorkTypeTemplate? workTypeTemplate = null;
        if (request.WorkTypeTemplateId.HasValue)
        {
            workTypeTemplate = await _db.WorkTypeTemplates
                .FirstOrDefaultAsync(s => s.Id == request.WorkTypeTemplateId.Value && s.IsActive);
            if (workTypeTemplate is null)
                return BadRequest(new { message = "Вид работ не найден" });
        }

        var workPricePerUnit = request.WorkPricePerUnit ?? workTypeTemplate?.BasePrice ?? 0m;
        var workQuantity = request.WorkQuantity;

        var stage = new TaskStage
        {
            Id = id,
            TaskId = request.TaskId,
            Name = request.Name,
            Description = request.Description,
            WorkTypeTemplateId = request.WorkTypeTemplateId,
            WorkTypeNameSnapshot = workTypeTemplate?.Name,
            WorkTypeDescriptionSnapshot = workTypeTemplate?.Description,
            WorkUnitSnapshot = workTypeTemplate?.Unit,
            WorkQuantity = workQuantity,
            WorkPricePerUnit = workPricePerUnit,
            AssignedUserId = request.AssignedUserId,
            DueDate = request.DueDate,
            Status = StageStatus.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TaskStages.Add(stage);

        var workTypeItems = request.WorkTypeItems ?? [];
        if (workTypeItems.Count > 0)
        {
            var ids = workTypeItems.Select(x => x.WorkTypeTemplateId).Distinct().ToList();
            var templates = await _db.WorkTypeTemplates
                .Where(s => ids.Contains(s.Id) && s.IsActive)
                .ToDictionaryAsync(s => s.Id);
            if (templates.Count != ids.Count)
                return BadRequest(new { message = "Один или несколько видов работ не найдены" });

            foreach (var item in workTypeItems)
            {
                var tpl = templates[item.WorkTypeTemplateId];
                _db.StageWorkTypes.Add(new StageWorkType
                {
                    StageId = stage.Id,
                    WorkTypeTemplateId = tpl.Id,
                    WorkTypeNameSnapshot = tpl.Name,
                    WorkTypeDescriptionSnapshot = tpl.Description,
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

    /// <summary>Обновить данные этапа.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskStageResponse>> Update(Guid id, [FromBody] UpdateStageRequest request)
    {
        var stage = await _db.TaskStages.FindAsync(id);
        if (stage is null) return NotFound();

        var restoringFromArchive = stage.IsArchived && !request.IsArchived;
        if (!request.IsArchived && !DueDatePolicy.IsAllowed(request.DueDate) && !restoringFromArchive)
            return BadRequest(new { message = DueDatePolicy.PastNotAllowedMessage });

        var oldStatus = stage.Status;

        stage.Name = request.Name;
        stage.Description = request.Description;
        stage.WorkTypeTemplateId = request.WorkTypeTemplateId;
        if (request.WorkTypeTemplateId.HasValue)
        {
            var workTypeTemplate = await _db.WorkTypeTemplates.FirstOrDefaultAsync(s => s.Id == request.WorkTypeTemplateId.Value);
            if (workTypeTemplate is null)
                return BadRequest(new { message = "Вид работ не найден" });

            stage.WorkTypeNameSnapshot = workTypeTemplate.Name;
            stage.WorkTypeDescriptionSnapshot = workTypeTemplate.Description;
            stage.WorkUnitSnapshot = workTypeTemplate.Unit;
        }
        else
        {
            stage.WorkTypeNameSnapshot = null;
            stage.WorkTypeDescriptionSnapshot = null;
            stage.WorkUnitSnapshot = null;
        }
        stage.WorkQuantity = request.WorkQuantity;
        stage.WorkPricePerUnit = request.WorkPricePerUnit;
        if (request.WorkTypeItems is { } workTypeItems)
        {
            if (workTypeItems.Count > 0)
            {
                var ids = workTypeItems.Select(x => x.WorkTypeTemplateId).Distinct().ToList();
                var templates = await _db.WorkTypeTemplates
                    .Where(s => ids.Contains(s.Id) && s.IsActive)
                    .ToDictionaryAsync(s => s.Id);
                if (templates.Count != ids.Count)
                    return BadRequest(new { message = "Один или несколько видов работ не найдены" });

                var existingWorkTypes = await _db.StageWorkTypes.Where(x => x.StageId == id).ToListAsync();
                _db.StageWorkTypes.RemoveRange(existingWorkTypes);
                foreach (var item in workTypeItems)
                {
                    var tpl = templates[item.WorkTypeTemplateId];
                    _db.StageWorkTypes.Add(new StageWorkType
                    {
                        Id = Guid.NewGuid(),
                        StageId = id,
                        WorkTypeTemplateId = tpl.Id,
                        WorkTypeNameSnapshot = tpl.Name,
                        WorkTypeDescriptionSnapshot = tpl.Description,
                        UnitSnapshot = tpl.Unit,
                        Quantity = item.Quantity,
                        PricePerUnit = item.PricePerUnit ?? tpl.BasePrice
                    });
                }
            }
            else if (!request.WorkTypeTemplateId.HasValue)
            {
                var existingWorkTypes = await _db.StageWorkTypes.Where(x => x.StageId == id).ToListAsync();
                _db.StageWorkTypes.RemoveRange(existingWorkTypes);
            }
        }
        stage.AssignedUserId = request.AssignedUserId;
        stage.Status = request.Status;
        stage.DueDate = request.DueDate;
        stage.IsMarkedForDeletion = request.IsMarkedForDeletion;
        stage.IsArchived = request.IsArchived;
        stage.UpdatedAt = DateTime.UtcNow;

        // Иначе этап снимается с архива, а задача остаётся IsArchived=true — клиент при pull снова «замораживает» задачу.
        if (restoringFromArchive)
        {
            var task = await _db.Tasks.FindAsync(stage.TaskId);
            if (task is not null && task.IsArchived)
            {
                task.IsArchived = false;
                task.UpdatedAt = DateTime.UtcNow;
            }
        }

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

    /// <summary>Удалить этап задачи.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var stage = await _db.TaskStages.FindAsync(id);
        if (stage is null) return NotFound();

        _db.TaskStages.Remove(stage);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Добавить материал к этапу.</summary>
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

        var unitPrice = request.PricePerUnit ?? material.Cost ?? 0m;

        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
            if (request.PricePerUnit.HasValue)
                existing.PricePerUnit = request.PricePerUnit.Value;
        }
        else
        {
            existing = new StageMaterial
            {
                StageId = id,
                MaterialId = request.MaterialId,
                Quantity = request.Quantity,
                PricePerUnit = unitPrice
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

    /// <summary>Удалить материал из этапа.</summary>
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
            .Include(s => s.StageWorkTypes)
            .Include(s => s.StageMaterials)
                .ThenInclude(sm => sm.Material)
            .Include(s => s.Files)
                .ThenInclude(f => f.UploadedBy)
            .FirstOrDefaultAsync(s => s.Id == id);

    private static TaskStageResponse MapToResponse(TaskStage s) =>
        new(
            s.Id, s.TaskId, s.Name, s.Description,
            s.WorkTypeTemplateId,
            s.WorkTypeNameSnapshot,
            s.WorkTypeDescriptionSnapshot,
            s.WorkUnitSnapshot,
            s.WorkQuantity,
            s.WorkPricePerUnit,
            (s.StageWorkTypes.Count > 0
                ? s.StageWorkTypes.Sum(ss => ss.Quantity * ss.PricePerUnit)
                : s.WorkQuantity * s.WorkPricePerUnit),
            s.StageWorkTypes.Select(ss => new StageWorkTypeResponse(
                ss.Id,
                ss.WorkTypeTemplateId,
                ss.WorkTypeNameSnapshot,
                ss.WorkTypeDescriptionSnapshot,
                ss.UnitSnapshot,
                ss.Quantity,
                ss.PricePerUnit,
                ss.Quantity * ss.PricePerUnit)).ToList(),
            s.StageMaterials.Sum(sm => sm.Quantity * sm.PricePerUnit),
            (s.StageWorkTypes.Count > 0
                ? s.StageWorkTypes.Sum(ss => ss.Quantity * ss.PricePerUnit)
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
