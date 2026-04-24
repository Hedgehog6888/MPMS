using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Категории материалов для складского учёта.</summary>
[ApiController]
[Route("api/material-categories")]
[Authorize]
public class MaterialCategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public MaterialCategoriesController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Получить список категорий материалов.</summary>
    [HttpGet]
    public async Task<ActionResult<List<MaterialCategoryResponse>>> GetAll()
    {
        var list = await _db.MaterialCategories
            .OrderBy(c => c.Name)
            .ToListAsync();
        return Ok(_mapper.Map<List<MaterialCategoryResponse>>(list));
    }

    /// <summary>Создать категорию материалов.</summary>
    [HttpPost]
    public async Task<ActionResult<MaterialCategoryResponse>> Create([FromBody] CreateMaterialCategoryRequest request)
    {
        var normalizedName = request.Name.Trim();
        var existingByName = await _db.MaterialCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == normalizedName);
        if (existingByName is not null)
            return Ok(_mapper.Map<MaterialCategoryResponse>(existingByName));

        var entity = _mapper.Map<MaterialCategory>(request);
        entity.Id = request.Id ?? Guid.NewGuid();
        entity.Name = normalizedName;

        try
        {
            _db.MaterialCategories.Add(entity);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            var existing = await _db.MaterialCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Name == normalizedName);
            if (existing is not null)
                return Ok(_mapper.Map<MaterialCategoryResponse>(existing));

            return Conflict(new { message = "РљР°С‚РµРіРѕСЂРёСЏ СЃ С‚Р°РєРёРј РёРјРµРЅРµРј СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚" });
        }

        return CreatedAtAction(nameof(GetAll), new { id = entity.Id },
            _mapper.Map<MaterialCategoryResponse>(entity));
    }
}
