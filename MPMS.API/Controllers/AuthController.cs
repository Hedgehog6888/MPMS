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

/// <summary>Аутентификация, регистрация и получение данных текущего пользователя.</summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IJwtService _jwt;
    private readonly IMapper _mapper;

    public AuthController(ApplicationDbContext db, IJwtService jwt, IMapper mapper)
    {
        _db = db;
        _jwt = jwt;
        _mapper = mapper;
    }

    /// <summary>Выполнить вход по логину и получить JWT-токен.</summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "РќРµРІРµСЂРЅС‹Р№ Р»РѕРіРёРЅ РёР»Рё РїР°СЂРѕР»СЊ" });

        if (user.IsBlocked)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Учётная запись заблокирована" });

        return Ok(await BuildAuthResponseAsync(user));
    }

    /// <summary>Зарегистрировать нового пользователя.</summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return Conflict(new { message = "Пользователь с таким логином уже существует" });

        var roleExists = await _db.Roles.AnyAsync(r => r.Id == request.RoleId);
        if (!roleExists)
            return BadRequest(new { message = "Указанная роль не существует" });

        var user = _mapper.Map<User>(request);
        user.FirstName = user.FirstName.Trim();
        user.LastName = user.LastName.Trim();
        user.Username = user.Username.Trim();
        user.Email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Role).LoadAsync();

        return Created($"/api/users/{user.Id}", await BuildAuthResponseAsync(user));
    }

    /// <summary>Обновить JWT-токен с помощью Refresh Token.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshRequest request)
    {
        var principal = _jwt.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
            return BadRequest(new { message = "Некорректный токен доступа" });

        var userIdStr = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdStr == null || !Guid.TryParse(userIdStr, out var userId))
            return BadRequest(new { message = "Некорректный токен доступа" });

        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || user.RefreshToken != request.RefreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            return Unauthorized(new { message = "Сессия истекла или неверный токен обновления" });

        if (user.IsBlocked)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Учётная запись заблокирована" });

        return Ok(await BuildAuthResponseAsync(user));
    }

    /// <summary>Получить профиль текущего авторизованного пользователя.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> GetMe()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null) return NotFound();

        return Ok(_mapper.Map<UserResponse>(user));
    }

    /// <summary>Изменить пароль текущего пользователя.</summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(userId);

        if (user is null) return NotFound();
        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest(new { message = "РўРµРєСѓС‰РёР№ РїР°СЂРѕР»СЊ РЅРµРІРµСЂРµРЅ" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Получить список доступных ролей для формы регистрации и администрирования.</summary>
    [HttpGet("roles")]
    public async Task<ActionResult<List<object>>> GetRoles()
    {
        var roles = await _db.Roles
            .Select(r => new { r.Id, r.Name, r.Description })
            .ToListAsync();
        return Ok(roles);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user)
    {
        var token = _jwt.GenerateToken(user);
        var refreshToken = _jwt.GenerateRefreshToken();
        
        user.RefreshToken = refreshToken;
        var days = int.Parse(_db.Database.GetDbConnection().Database == "MPMS" ? "7" : "7"); // Default 7 days
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _db.SaveChangesAsync();

        return new AuthResponse(
            user.Id,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Username,
            user.Role.Name,
            token,
            _jwt.GetExpiryTime(),
            refreshToken);
    }
}
