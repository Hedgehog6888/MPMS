using AutoMapper;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/materials/{materialId:guid}/stock-movements")]
[Authorize]
public class MaterialStockMovementsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public MaterialStockMovementsController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<List<MaterialStockMovementResponse>>> GetList(Guid materialId)
    {
        var exists = await _db.Materials.AnyAsync(m => m.Id == materialId);
        if (!exists) return NotFound();

        var list = await _db.MaterialStockMovements
            .Where(x => x.MaterialId == materialId)
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();

        return Ok(_mapper.Map<List<MaterialStockMovementResponse>>(list));
    }

    [HttpPost]
    public async Task<ActionResult<MaterialStockMovementResponse>> Record(Guid materialId,
        [FromBody] RecordMaterialStockRequest request)
    {
        var material = await _db.Materials.FirstOrDefaultAsync(m => m.Id == materialId);
        if (material is null) return NotFound();

        var newQty = material.Quantity + request.Delta;
        if (newQty < 0)
            return BadRequest(new { message = "Остаток не может стать отрицательным" });

        if (request.ProjectId.HasValue &&
            !await _db.Projects.AnyAsync(p => p.Id == request.ProjectId.Value))
            return BadRequest(new { message = "Проект не найден" });

        if (request.TaskId.HasValue &&
            !await _db.Tasks.AnyAsync(t => t.Id == request.TaskId.Value))
            return BadRequest(new { message = "Задача не найдена" });

        var now = DateTime.UtcNow;
        var movement = new MaterialStockMovement
        {
            MaterialId = materialId,
            OccurredAt = now,
            Delta = request.Delta,
            QuantityAfter = newQty,
            OperationType = request.OperationType,
            Comment = request.Comment,
            UserId = CurrentUserId(),
            ProjectId = request.ProjectId,
            TaskId = request.TaskId
        };

        material.Quantity = newQty;
        material.UpdatedAt = now;

        _db.MaterialStockMovements.Add(movement);
        await _db.SaveChangesAsync();

        return Ok(_mapper.Map<MaterialStockMovementResponse>(movement));
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
