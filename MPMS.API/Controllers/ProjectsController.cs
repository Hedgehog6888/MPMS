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
public class ProjectsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IActivityLogService _log;
    private readonly IMapper _mapper;

    public ProjectsController(ApplicationDbContext db, IActivityLogService log, IMapper mapper)
    {
        _db = db;
        _log = log;
        _mapper = mapper;
    }

    /// <summary>Получить список проектов с необязательной фильтрацией по статусу и поиску.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ProjectListResponse>>> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        var query = _db.Projects
            .Include(p => p.Manager)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ProjectStatus>(status, true, out var parsedStatus))
            query = query.Where(p => p.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search));

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return Ok(_mapper.Map<List<ProjectListResponse>>(projects));
    }

    /// <summary>Получить проект по идентификатору с полной сводной информацией.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> GetById(Guid id)
    {
        var p = await _db.Projects
            .Include(p => p.Manager)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (p is null) return NotFound();

        return Ok(_mapper.Map<ProjectResponse>(p));
    }

    /// <summary>Создать новый проект.</summary>
    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest request)
    {
        var id = request.Id ?? Guid.NewGuid();

        // Повторный POST с тем же Id (очередь синхронизации / ретраи) — без конфликта PK
        if (await _db.Projects.AnyAsync(p => p.Id == id))
            return await GetById(id);

        var managerExists = await _db.Users.AnyAsync(u => u.Id == request.ManagerId);
        if (!managerExists)
            return BadRequest(new { message = "Менеджер не найден" });

        var project = _mapper.Map<Project>(request);
        project.Id = id;
        project.CreatedAt = DateTime.UtcNow;
        project.UpdatedAt = DateTime.UtcNow;

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        await _log.LogAsync(CurrentUserId(), ActivityActionType.Created,
            ActivityEntityType.Project, project.Id, $"Создан проект: {project.Name}");

        return CreatedAtAction(nameof(GetById), new { id = project.Id },
            await GetById(project.Id));
    }

    /// <summary>Обновить данные проекта.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Update(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is null) return NotFound();

        var managerExists = await _db.Users.AnyAsync(u => u.Id == request.ManagerId);
        if (!managerExists)
            return BadRequest(new { message = "Менеджер не найден" });

        _mapper.Map(request, project);
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _log.LogAsync(CurrentUserId(), ActivityActionType.Updated,
            ActivityEntityType.Project, project.Id, $"Обновлён проект: {project.Name}");

        return Ok(await GetById(id));
    }

    /// <summary>Удалить проект вместе со связанными данными.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ok = await ProjectCascadeDelete.TryDeleteProjectGraphAsync(_db, id);
        if (!ok) return NotFound();

        return NoContent();
    }

    /// <summary>Получить список участников проекта.</summary>
    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<UserResponse>>> GetMembers(Guid id)
    {
        var members = await _db.ProjectMembers
            .Where(pm => pm.ProjectId == id)
            .Include(pm => pm.User)
            .ThenInclude(u => u.Role)
            .ToListAsync();

        return Ok(_mapper.Map<List<UserResponse>>(members.Select(pm => pm.User).ToList()));
    }

    /// <summary>Добавить пользователя в состав проекта.</summary>
    [HttpPost("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> AddMember(Guid id, Guid userId)
    {
        var projectExists = await _db.Projects.AnyAsync(p => p.Id == id);
        if (!projectExists) return NotFound(new { message = "Проект не найден" });

        var userExists = await _db.Users.AnyAsync(u => u.Id == userId);
        if (!userExists) return NotFound(new { message = "Пользователь не найден" });

        var alreadyMember = await _db.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == id && pm.UserId == userId);
        if (alreadyMember) return Conflict(new { message = "Пользователь уже является участником" });

        _db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Удалить пользователя из состава проекта.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        var member = await _db.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == id && pm.UserId == userId);

        if (member is null) return NotFound();

        _db.ProjectMembers.Remove(member);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
