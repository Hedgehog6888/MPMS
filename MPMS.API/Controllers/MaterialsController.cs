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

    public MaterialsController(ApplicationDbContext db)
    {
        _db = db;
    }

    private static MaterialResponse ToDto(Material m) => new(
        m.Id, m.Name, m.Unit, m.Description, m.Quantity,
        m.Cost, m.InventoryNumber,
        m.CategoryId, m.Category?.Name, m.ImagePath, m.CreatedAt, m.UpdatedAt,
        m.IsWrittenOff, m.WrittenOffAt, m.WrittenOffComment,
        m.IsArchived);

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
            .Select(m => new MaterialResponse(
                m.Id,
                m.Name,
                m.Unit,
                m.Description,
                m.Quantity,
                m.Cost,
                m.InventoryNumber,
                m.CategoryId,
                m.Category != null ? m.Category.Name : null,
                m.ImagePath,
                m.CreatedAt,
                m.UpdatedAt,
                m.IsWrittenOff,
                m.WrittenOffAt,
                m.WrittenOffComment,
                m.IsArchived))
            .ToListAsync();

        return Ok(materials);
    }

    /// <summary>Get material by ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> GetById(Guid id)
    {
        var m = await _db.Materials.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
        if (m is null) return NotFound();
        return Ok(ToDto(m));
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
        var material = new Material
        {
            Id = id,
            Name = request.Name,
            Unit = request.Unit,
            Description = request.Description,
            Quantity = request.InitialQuantity < 0 ? 0 : request.InitialQuantity,
            Cost = request.Cost,
            InventoryNumber = request.InventoryNumber,
            CategoryId = request.CategoryId,
            ImagePath = request.ImagePath,
            CreatedAt = now,
            UpdatedAt = now
        };

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
        return CreatedAtAction(nameof(GetById), new { id = material.Id }, ToDto(material));
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

        material.Name = request.Name;
        material.Unit = request.Unit;
        material.Description = request.Description;
        material.Cost = request.Cost;
        material.InventoryNumber = request.InventoryNumber;
        material.CategoryId = request.CategoryId;
        material.ImagePath = request.ImagePath;
        material.IsWrittenOff = request.IsWrittenOff;
        material.WrittenOffAt = request.WrittenOffAt;
        material.WrittenOffComment = request.WrittenOffComment;
        material.IsArchived = request.IsArchived;
        material.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _db.Entry(material).Reference(x => x.Category).LoadAsync();
        return Ok(ToDto(material));
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
