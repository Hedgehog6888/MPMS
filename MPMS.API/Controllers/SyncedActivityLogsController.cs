using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/synced-activity-logs")]
[Authorize]
public class SyncedActivityLogsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SyncedActivityLogsController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<SyncedActivityLogResponse>>> GetAll([FromQuery] DateTime? since)
    {
        var q = _db.SyncedActivityLogs.AsQueryable();
        if (since.HasValue) q = q.Where(l => l.CreatedAt > since.Value);

        var list = await q
            .OrderByDescending(l => l.CreatedAt)
            .Take(5000)
            .Select(l => new SyncedActivityLogResponse(
                l.Id, l.UserId, l.ActorRole, l.UserName, l.UserInitials, l.UserColor,
                l.ActionType, l.ActionText, l.EntityType, l.EntityId, l.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }

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
            return Ok(new SyncedActivityLogResponse(
                e.Id, e.UserId, e.ActorRole, e.UserName, e.UserInitials, e.UserColor,
                e.ActionType, e.ActionText, e.EntityType, e.EntityId, e.CreatedAt));
        }

        var entry = new SyncedActivityLog
        {
            Id = request.Id,
            UserId = request.UserId,
            ActorRole = request.ActorRole,
            UserName = request.UserName.Trim(),
            UserInitials = request.UserInitials,
            UserColor = request.UserColor,
            ActionType = request.ActionType,
            ActionText = request.ActionText.Trim(),
            EntityType = request.EntityType.Trim(),
            EntityId = request.EntityId,
            CreatedAt = request.CreatedAt
        };

        _db.SyncedActivityLogs.Add(entry);
        await _db.SaveChangesAsync();

        return Created($"/api/synced-activity-logs/{entry.Id}", new SyncedActivityLogResponse(
            entry.Id, entry.UserId, entry.ActorRole, entry.UserName, entry.UserInitials, entry.UserColor,
            entry.ActionType, entry.ActionText, entry.EntityType, entry.EntityId, entry.CreatedAt));
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
