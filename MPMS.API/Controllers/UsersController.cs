using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;
using MPMS.API.Services;

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
        {
            var s = search.Trim();
            query = query.Where(u =>
                (u.FirstName + " " + u.LastName).Contains(s) ||
                u.Username.Contains(s) ||
                (u.Email != null && u.Email.Contains(s)));
        }

        var users = await query
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new UserResponse(u.Id, u.FirstName, u.LastName, u.Username, u.Email, u.Role.Name, u.CreatedAt, u.AvatarData, u.SubRole, u.AdditionalSubRoles))
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>Create user (admin only)</summary>
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create([FromBody] CreateUserRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return Conflict(new { message = "Пользователь с таким логином уже существует" });

        var roleExists = await _db.Roles.AnyAsync(r => r.Id == request.RoleId);
        if (!roleExists)
            return BadRequest(new { message = "Указанная роль не существует" });

        var fullName = $"{request.FirstName.Trim()} {request.LastName.Trim()}".Trim();
        var avatarData = request.AvatarData is { Length: > 0 }
            ? request.AvatarData
            : AvatarGenerator.GenerateInitialsAvatar(fullName);

        var user = new User
        {
            Id = request.Id ?? Guid.NewGuid(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Username = request.Username.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = request.RoleId,
            SubRole = string.IsNullOrWhiteSpace(request.SubRole) ? null : request.SubRole.Trim(),
            AdditionalSubRoles = string.IsNullOrWhiteSpace(request.AdditionalSubRoles) ? null : request.AdditionalSubRoles.Trim(),
            AvatarData = avatarData,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Role).LoadAsync();

        return Created($"/api/users/{user.Id}", new UserResponse(user.Id, user.FirstName, user.LastName,
            user.Username, user.Email, user.Role.Name, user.CreatedAt, user.AvatarData, user.SubRole, user.AdditionalSubRoles));
    }

    /// <summary>Update user (admin only)</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        if (await _db.Users.AnyAsync(u => u.Username == request.Username && u.Id != id))
            return Conflict(new { message = "Пользователь с таким логином уже существует" });

        var roleExists = await _db.Roles.AnyAsync(r => r.Id == request.RoleId);
        if (!roleExists)
            return BadRequest(new { message = "Указанная роль не существует" });

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Username = request.Username.Trim();
        user.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        user.RoleId = request.RoleId;
        user.SubRole = string.IsNullOrWhiteSpace(request.SubRole) ? null : request.SubRole.Trim();
        user.AdditionalSubRoles = string.IsNullOrWhiteSpace(request.AdditionalSubRoles) ? null : request.AdditionalSubRoles.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(request.NewPassword))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Role).LoadAsync();

        return Ok(new UserResponse(user.Id, user.FirstName, user.LastName,
            user.Username, user.Email, user.Role.Name, user.CreatedAt, user.AvatarData, user.SubRole, user.AdditionalSubRoles));
    }

    /// <summary>Upload avatar for current user or specified user (admin can upload for any user).</summary>
    [HttpPut("{id:guid}/avatar")]
    public async Task<ActionResult> UploadAvatar(Guid id, [FromBody] UploadAvatarRequest request)
    {
        var currentUserId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (Guid?)null;
        var isAdmin = User.IsInRole("Administrator");
        if (currentUserId != id && !isAdmin)
            return Forbid();

        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        if (request.AvatarData is null || request.AvatarData.Length == 0)
            return BadRequest(new { message = "Данные аватара не переданы" });
        if (request.AvatarData.Length > 512 * 1024) // 512 KB max
            return BadRequest(new { message = "Размер аватара не должен превышать 512 КБ" });

        user.AvatarData = request.AvatarData;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Delete user (admin only). Fails if user is a project manager.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        if (await _db.Projects.AnyAsync(p => p.ManagerId == id))
            return BadRequest(new { message = "Нельзя удалить пользователя, который является руководителем проекта" });

        await _db.ProjectMembers.Where(m => m.UserId == id).ExecuteDeleteAsync();
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
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
                l.Id, l.UserId, l.User.FirstName + " " + l.User.LastName,
                l.ActionType.ToString(), l.EntityType.ToString(),
                l.EntityId, l.Description, l.CreatedAt))
            .ToListAsync();

        return Ok(logs);
    }
}
