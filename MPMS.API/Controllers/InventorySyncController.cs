using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Bulk read endpoints for offline sync.</summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventorySyncController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public InventorySyncController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    [HttpGet("material-stock-movements")]
    public async Task<ActionResult<List<MaterialStockMovementResponse>>> GetAllMaterialStockMovements()
    {
        var list = await _db.MaterialStockMovements
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();
        return Ok(_mapper.Map<List<MaterialStockMovementResponse>>(list));
    }

    [HttpGet("equipment")]
    public async Task<ActionResult<List<EquipmentResponse>>> GetAllEquipment()
    {
        var list = await _db.Equipments
            .Include(e => e.Category)
            .OrderBy(e => e.Name)
            .ToListAsync();
        return Ok(_mapper.Map<List<EquipmentResponse>>(list));
    }

    [HttpGet("equipment-history")]
    public async Task<ActionResult<List<EquipmentHistoryEntryResponse>>> GetAllEquipmentHistory()
    {
        var list = await _db.EquipmentHistoryEntries
            .OrderByDescending(x => x.OccurredAt)
            .ToListAsync();
        return Ok(_mapper.Map<List<EquipmentHistoryEntryResponse>>(list));
    }
}
