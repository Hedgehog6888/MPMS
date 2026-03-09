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

    public ProjectsController(ApplicationDbContext db, IActivityLogService log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Get all projects (with optional status filter)</summary>
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
            .Select(p => new ProjectListResponse(
                p.Id, p.Name, p.Client,
                p.StartDate, p.EndDate,
                p.Status.ToString(), p.Manager.Name))
            .ToListAsync();

        return Ok(projects);
    }

    /// <summary>Get project by ID with full details</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> GetById(Guid id)
    {
        var p = await _db.Projects
            .Include(p => p.Manager)
            .Include(p => p.Tasks)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (p is null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overdue = p.Tasks.Count(t =>
            t.DueDate < today && t.Status != Models.TaskStatus.Completed);

        return Ok(new ProjectResponse(
            p.Id, p.Name, p.Description, p.Client, p.Address,
            p.StartDate, p.EndDate, p.Status.ToString(),
            p.ManagerId, p.Manager.Name,
            p.Tasks.Count,
            p.Tasks.Count(t => t.Status == Models.TaskStatus.Completed),
            p.Tasks.Count(t => t.Status == Models.TaskStatus.InProgress),
            overdue,
            p.CreatedAt, p.UpdatedAt));
    }

    /// <summary>Create a new project</summary>
    [HttpPost]
    public async Task<ActionResult<ProjectResponse>> Create([FromBody] CreateProjectRequest request)
    {
        var managerExists = await _db.Users.AnyAsync(u => u.Id == request.ManagerId);
        if (!managerExists)
            return BadRequest(new { message = "Менеджер не найден" });

        var project = new Project
        {
            Name = request.Name,
            Description = request.Description,
            Client = request.Client,
            Address = request.Address,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = ProjectStatus.Planning,
            ManagerId = request.ManagerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        await _log.LogAsync(CurrentUserId(), ActivityActionType.Created,
            ActivityEntityType.Project, project.Id, $"Создан проект: {project.Name}");

        return CreatedAtAction(nameof(GetById), new { id = project.Id },
            await GetById(project.Id));
    }

    /// <summary>Update project</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectResponse>> Update(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is null) return NotFound();

        var managerExists = await _db.Users.AnyAsync(u => u.Id == request.ManagerId);
        if (!managerExists)
            return BadRequest(new { message = "Менеджер не найден" });

        project.Name = request.Name;
        project.Description = request.Description;
        project.Client = request.Client;
        project.Address = request.Address;
        project.StartDate = request.StartDate;
        project.EndDate = request.EndDate;
        project.Status = request.Status;
        project.ManagerId = request.ManagerId;
        project.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _log.LogAsync(CurrentUserId(), ActivityActionType.Updated,
            ActivityEntityType.Project, project.Id, $"Обновлён проект: {project.Name}");

        return Ok(await GetById(id));
    }

    /// <summary>Delete project</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var project = await _db.Projects.FindAsync(id);
        if (project is null) return NotFound();

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Get project members</summary>
    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<List<UserResponse>>> GetMembers(Guid id)
    {
        var members = await _db.ProjectMembers
            .Where(pm => pm.ProjectId == id)
            .Include(pm => pm.User)
            .ThenInclude(u => u.Role)
            .Select(pm => new UserResponse(
                pm.User.Id, pm.User.Name, pm.User.Email,
                pm.User.Role.Name, pm.User.CreatedAt))
            .ToListAsync();

        return Ok(members);
    }

    /// <summary>Add member to project</summary>
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

    /// <summary>Remove member from project</summary>
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
