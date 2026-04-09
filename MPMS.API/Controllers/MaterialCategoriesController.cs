using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/material-categories")]
[Authorize]
public class MaterialCategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MaterialCategoriesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<MaterialCategoryResponse>>> GetAll()
    {
        var list = await _db.MaterialCategories
            .OrderBy(c => c.Name)
            .Select(c => new MaterialCategoryResponse(c.Id, c.Name))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<MaterialCategoryResponse>> Create([FromBody] CreateMaterialCategoryRequest request)
    {
        var normalizedName = request.Name.Trim();
        var existingByName = await _db.MaterialCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == normalizedName);
        if (existingByName is not null)
            return Ok(new MaterialCategoryResponse(existingByName.Id, existingByName.Name));

        var entity = new MaterialCategory
        {
            Id = request.Id ?? Guid.NewGuid(),
            Name = normalizedName
        };
        try
        {
            _db.MaterialCategories.Add(entity);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Idempotent create for sync retries/races: return existing category by name.
            var existing = await _db.MaterialCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == normalizedName);
            if (existing is not null)
                return Ok(new MaterialCategoryResponse(existing.Id, existing.Name));
            return Conflict(new { message = "Категория с таким именем уже существует" });
        }

        return CreatedAtAction(nameof(GetAll), new { id = entity.Id },
            new MaterialCategoryResponse(entity.Id, entity.Name));
    }
}
