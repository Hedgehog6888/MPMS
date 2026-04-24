using AutoMapper;
using System.Security.Claims;
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
    private readonly IMapper _mapper;

    public UsersController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Получить список пользователей, в том числе для выбора исполнителей.</summary>
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
            .ToListAsync();

        return Ok(_mapper.Map<List<UserResponse>>(users));
    }

    /// <summary>Создать нового пользователя. Доступно администратору.</summary>
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

        var user = _mapper.Map<User>(request);
        user.Id = request.Id ?? Guid.NewGuid();
        user.FirstName = user.FirstName.Trim();
        user.LastName = user.LastName.Trim();
        user.Username = user.Username.Trim();
        user.Email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();
        user.SubRole = string.IsNullOrWhiteSpace(user.SubRole) ? null : user.SubRole.Trim();
        user.AdditionalSubRoles = string.IsNullOrWhiteSpace(user.AdditionalSubRoles) ? null : user.AdditionalSubRoles.Trim();
        user.HomeAddress = string.IsNullOrWhiteSpace(user.HomeAddress) ? null : user.HomeAddress.Trim();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        user.AvatarData = avatarData;
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Role).LoadAsync();

        return Created($"/api/users/{user.Id}", _mapper.Map<UserResponse>(user));
    }

    /// <summary>Обновить данные пользователя. Доступно администратору или самому пользователю.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var currentUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid) ? cid : (Guid?)null;
        var isAdmin = User.IsInRole("Administrator");
        if (!isAdmin && currentUserId != id)
            return Forbid();

        if (request.RoleId != user.RoleId)
            return BadRequest(new { message = "Нельзя изменить роль пользователя" });

        if (await _db.Users.AnyAsync(u => u.Username == request.Username && u.Id != id))
            return Conflict(new { message = "Пользователь с таким логином уже существует" });

        var roleExists = await _db.Roles.AnyAsync(r => r.Id == request.RoleId);
        if (!roleExists)
            return BadRequest(new { message = "Указанная роль не существует" });

        _mapper.Map(request, user);
        user.FirstName = user.FirstName.Trim();
        user.LastName = user.LastName.Trim();
        user.Username = user.Username.Trim();
        user.Email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();
        user.SubRole = string.IsNullOrWhiteSpace(user.SubRole) ? null : user.SubRole.Trim();
        user.AdditionalSubRoles = string.IsNullOrWhiteSpace(user.AdditionalSubRoles) ? null : user.AdditionalSubRoles.Trim();
        user.HomeAddress = string.IsNullOrWhiteSpace(user.HomeAddress) ? null : user.HomeAddress.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(request.NewPassword))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        if (request.IsBlocked.HasValue && isAdmin)
        {
            await _db.Entry(user).Reference(u => u.Role).LoadAsync();
            var targetIsAdminRole = user.Role.Name.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
                || user.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            if (request.IsBlocked.Value && targetIsAdminRole && !user.IsBlocked)
                return BadRequest(new { message = "Нельзя заблокировать пользователя с ролью администратора" });

            user.IsBlocked = request.IsBlocked.Value;
            user.BlockedAt = request.IsBlocked.Value ? DateTime.UtcNow : null;
            if (!request.IsBlocked.Value)
                user.BlockedReason = null;
            else if (!string.IsNullOrWhiteSpace(request.BlockedReason))
                user.BlockedReason = request.BlockedReason.Trim();
        }

        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Role).LoadAsync();

        return Ok(_mapper.Map<UserResponse>(user));
    }

    /// <summary>Загрузить аватар для текущего пользователя или любого пользователя от имени администратора.</summary>
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

    /// <summary>Удалить пользователя. Недоступно для руководителей проектов и администраторов.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        if (user.Role.Name.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
            || user.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Нельзя удалить пользователя с ролью администратора" });

        if (await _db.Projects.AnyAsync(p => p.ManagerId == id))
            return BadRequest(new { message = "Нельзя удалить пользователя, который является руководителем проекта" });

        await _db.ProjectMembers.Where(m => m.UserId == id).ExecuteDeleteAsync();
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Получить журнал действий с фильтрами по пользователю, типу действия, сущности и периоду.</summary>
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
            .ToListAsync();

        return Ok(_mapper.Map<List<ActivityLogResponse>>(logs));
    }
}
