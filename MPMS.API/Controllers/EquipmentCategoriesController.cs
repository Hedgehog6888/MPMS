using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/equipment-categories")]
[Authorize]
public class EquipmentCategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public EquipmentCategoriesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<EquipmentCategoryResponse>>> GetAll()
    {
        var list = await _db.EquipmentCategories
            .OrderBy(c => c.Name)
            .Select(c => new EquipmentCategoryResponse(c.Id, c.Name))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentCategoryResponse>> Create([FromBody] CreateEquipmentCategoryRequest request)
    {
        var entity = new EquipmentCategory
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim()
        };
        try
        {
            _db.EquipmentCategories.Add(entity);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Категория с таким именем уже существует" });
        }

        return CreatedAtAction(nameof(GetAll), new { id = entity.Id },
            new EquipmentCategoryResponse(entity.Id, entity.Name));
    }
}
