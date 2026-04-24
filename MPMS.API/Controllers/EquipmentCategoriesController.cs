using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Категории оборудования для складского учёта.</summary>
[ApiController]
[Route("api/equipment-categories")]
[Authorize]
public class EquipmentCategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public EquipmentCategoriesController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Получить список категорий оборудования.</summary>
    [HttpGet]
    public async Task<ActionResult<List<EquipmentCategoryResponse>>> GetAll()
    {
        var list = await _db.EquipmentCategories
            .OrderBy(c => c.Name)
            .ToListAsync();
        return Ok(_mapper.Map<List<EquipmentCategoryResponse>>(list));
    }

    /// <summary>Создать категорию оборудования.</summary>
    [HttpPost]
    public async Task<ActionResult<EquipmentCategoryResponse>> Create([FromBody] CreateEquipmentCategoryRequest request)
    {
        var normalizedName = request.Name.Trim();
        var existingByName = await _db.EquipmentCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == normalizedName);
        if (existingByName is not null)
            return Ok(_mapper.Map<EquipmentCategoryResponse>(existingByName));

        var entity = _mapper.Map<EquipmentCategory>(request);
        entity.Id = request.Id ?? Guid.NewGuid();
        entity.Name = normalizedName;

        try
        {
            _db.EquipmentCategories.Add(entity);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            var existing = await _db.EquipmentCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == normalizedName);
            if (existing is not null)
                return Ok(_mapper.Map<EquipmentCategoryResponse>(existing));

            return Conflict(new { message = "РљР°С‚РµРіРѕСЂРёСЏ СЃ С‚Р°РєРёРј РёРјРµРЅРµРј СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚" });
        }

        return CreatedAtAction(nameof(GetAll), new { id = entity.Id },
            _mapper.Map<EquipmentCategoryResponse>(entity));
    }
}
