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

    /// <summary>Get all materials (with optional search)</summary>
    [HttpGet]
    public async Task<ActionResult<List<MaterialResponse>>> GetAll([FromQuery] string? search)
    {
        var query = _db.Materials.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => m.Name.Contains(search));

        var materials = await query
            .OrderBy(m => m.Name)
            .Select(m => new MaterialResponse(m.Id, m.Name, m.Unit, m.Description, m.CreatedAt))
            .ToListAsync();

        return Ok(materials);
    }

    /// <summary>Get material by ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> GetById(Guid id)
    {
        var m = await _db.Materials.FindAsync(id);
        if (m is null) return NotFound();
        return Ok(new MaterialResponse(m.Id, m.Name, m.Unit, m.Description, m.CreatedAt));
    }

    /// <summary>Create material</summary>
    [HttpPost]
    public async Task<ActionResult<MaterialResponse>> Create([FromBody] CreateMaterialRequest request)
    {
        var material = new Material
        {
            Id = request.Id ?? Guid.NewGuid(),
            Name = request.Name,
            Unit = request.Unit,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = material.Id },
            new MaterialResponse(material.Id, material.Name, material.Unit,
                material.Description, material.CreatedAt));
    }

    /// <summary>Update material</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MaterialResponse>> Update(Guid id, [FromBody] UpdateMaterialRequest request)
    {
        var material = await _db.Materials.FindAsync(id);
        if (material is null) return NotFound();

        material.Name = request.Name;
        material.Unit = request.Unit;
        material.Description = request.Description;

        await _db.SaveChangesAsync();

        return Ok(new MaterialResponse(material.Id, material.Name, material.Unit,
            material.Description, material.CreatedAt));
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
}
