using AutoMapper;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Лента действий, синхронизируемая с клиентским приложением.</summary>
[ApiController]
[Route("api/synced-activity-logs")]
[Authorize]
public class SyncedActivityLogsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public SyncedActivityLogsController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Получить синхронизированные события активности с фильтром по времени.</summary>
    [HttpGet]
    public async Task<ActionResult<List<SyncedActivityLogResponse>>> GetAll([FromQuery] DateTime? since)
    {
        var q = _db.SyncedActivityLogs.AsQueryable();
        if (since.HasValue) q = q.Where(l => l.CreatedAt > since.Value);

        var list = await q
            .OrderByDescending(l => l.CreatedAt)
            .Take(5000)
            .ToListAsync();
        return Ok(_mapper.Map<List<SyncedActivityLogResponse>>(list));
    }

    /// <summary>Создать новую запись в синхронизируемой ленте активности.</summary>
    [HttpPost]
    public async Task<ActionResult<SyncedActivityLogResponse>> Create([FromBody] CreateSyncedActivityLogRequest request)
    {
        var currentId = CurrentUserId();
        var isAdmin = User.IsInRole("Administrator");
        if (request.UserId.HasValue && request.UserId != currentId && !isAdmin)
            return Forbid();

        if (await _db.SyncedActivityLogs.AnyAsync(l => l.Id == request.Id))
        {
            var e = await _db.SyncedActivityLogs.FirstAsync(l => l.Id == request.Id);
            return Ok(_mapper.Map<SyncedActivityLogResponse>(e));
        }

        var entry = _mapper.Map<SyncedActivityLog>(request);
        entry.UserName = entry.UserName.Trim();
        entry.ActionText = entry.ActionText.Trim();
        entry.EntityType = entry.EntityType.Trim();

        _db.SyncedActivityLogs.Add(entry);
        await _db.SaveChangesAsync();

        return Created($"/api/synced-activity-logs/{entry.Id}", _mapper.Map<SyncedActivityLogResponse>(entry));
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
