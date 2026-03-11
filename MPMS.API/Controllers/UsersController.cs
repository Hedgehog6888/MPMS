using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public UsersController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Get all users (for assignee dropdowns)</summary>
    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetAll([FromQuery] string? search)
    {
        var query = _db.Users.Include(u => u.Role).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Name.Contains(search) || (u.Email != null && u.Email.Contains(search)));

        var users = await query
            .OrderBy(u => u.Name)
            .Select(u => new UserResponse(u.Id, u.Name, u.Username, u.Email, u.Role.Name, u.CreatedAt))
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>Get activity log (with filters)</summary>
    [HttpGet("/api/activity-log")]
    public async Task<ActionResult<List<ActivityLogResponse>>> GetActivityLog(
        [FromQuery] Guid? userId,
        [FromQuery] string? actionType,
        [FromQuery] string? entityType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.ActivityLogs
            .Include(l => l.User)
            .AsQueryable();

        if (userId.HasValue) query = query.Where(l => l.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(actionType) &&
            Enum.TryParse<Models.ActivityActionType>(actionType, true, out var action))
            query = query.Where(l => l.ActionType == action);
        if (!string.IsNullOrWhiteSpace(entityType) &&
            Enum.TryParse<Models.ActivityEntityType>(entityType, true, out var entity))
            query = query.Where(l => l.EntityType == entity);
        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to.Value);

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new ActivityLogResponse(
                l.Id, l.UserId, l.User.Name,
                l.ActionType.ToString(), l.EntityType.ToString(),
                l.EntityId, l.Description, l.CreatedAt))
            .ToListAsync();

        return Ok(logs);
    }
}
