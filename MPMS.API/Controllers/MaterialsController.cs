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
[Route("api/[controller]")]
[Authorize]
public class MaterialsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public MaterialsController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Get all materials (with optional search)</summary>
    [HttpGet]
    public async Task<ActionResult<List<MaterialResponse>>> GetAll([FromQuery] string? search)
    {
        var query = _db.Materials.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(m =>
                m.Name.Contains(s) ||
                (m.InventoryNumber != null && m.InventoryNumber.Contains(s)));
        }

        var materials = await query
            .OrderBy(m => m.Name)
            .ToListAsync();

        return Ok(_mapper.Map<List<MaterialResponse>>(materials));
    }

    /// <summary>Get material by ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> GetById(Guid id)
    {
        var m = await _db.Materials.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        return Ok(_mapper.Map<MaterialResponse>(m));
    }

    /// <summary>Create material</summary>
    [HttpPost]
    public async Task<ActionResult<MaterialResponse>> Create([FromBody] CreateMaterialRequest request)
    {
        var id = request.Id ?? Guid.NewGuid();

        if (await _db.Materials.AnyAsync(m => m.Id == id))
            return await GetById(id);

        if (request.CategoryId.HasValue)
        {
            var catOk = await _db.MaterialCategories.AnyAsync(c => c.Id == request.CategoryId.Value);
            if (!catOk) return BadRequest(new { message = "Категория материала не найдена" });
        }

        var now = DateTime.UtcNow;
        var material = _mapper.Map<Material>(request);
        material.Id = id;
        material.Quantity = request.InitialQuantity < 0 ? 0 : request.InitialQuantity;
        material.CreatedAt = now;
        material.UpdatedAt = now;

        _db.Materials.Add(material);

        if (material.Quantity != 0)
        {
            _db.MaterialStockMovements.Add(new MaterialStockMovement
            {
                MaterialId = material.Id,
                OccurredAt = now,
                Delta = material.Quantity,
                QuantityAfter = material.Quantity,
                OperationType = MaterialStockOperationType.Purchase,
                Comment = "Начальный остаток",
                UserId = CurrentUserId()
            });
        }

        await _db.SaveChangesAsync();

        await _db.Entry(material).Reference(x => x.Category).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = material.Id }, _mapper.Map<MaterialResponse>(material));
    }

    /// <summary>Update material</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> Update(Guid id, [FromBody] UpdateMaterialRequest request)
    {
        var material = await _db.Materials.FindAsync(id);
        if (material is null) return NotFound();

        if (request.CategoryId.HasValue)
        {
            var catOk = await _db.MaterialCategories.AnyAsync(c => c.Id == request.CategoryId.Value);
            if (!catOk) return BadRequest(new { message = "Категория материала не найдена" });
        }

        _mapper.Map(request, material);
        material.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.Entry(material).Reference(x => x.Category).LoadAsync();
        return Ok(_mapper.Map<MaterialResponse>(material));
    }

    /// <summary>Delete material</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var material = await _db.Materials.FindAsync(id);
        if (material is null) return NotFound();

        var inUse = await _db.StageMaterials.AnyAsync(sm => sm.MaterialId == id);
        if (inUse)
            return Conflict(new { message = "Материал используется в этапах работ и не может быть удалён" });

        _db.Materials.Remove(material);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
