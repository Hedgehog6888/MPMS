using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;
using MPMS.API.Services;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IActivityLogService _log;

    public TasksController(ApplicationDbContext db, IActivityLogService log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Get tasks (optional filters: projectId, status, assignedUserId, priority)</summary>
    [HttpGet]
    public async Task<ActionResult<List<TaskListResponse>>> GetAll(
        [FromQuery] Guid? projectId,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] Guid? assignedUserId,
        [FromQuery] string? search)
    {
        var query = _db.Tasks
            .Include(t => t.Project)
            .Include(t => t.AssignedUser)
            .Include(t => t.Stages)
            .AsQueryable();

        if (projectId.HasValue)
            query = query.Where(t => t.ProjectId == projectId.Value);

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<Models.TaskStatus>(status, true, out var parsedStatus))
            query = query.Where(t => t.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(priority) &&
            Enum.TryParse<TaskPriority>(priority, true, out var parsedPriority))
            query = query.Where(t => t.Priority == parsedPriority);

        if (assignedUserId.HasValue)
            query = query.Where(t => t.AssignedUserId == assignedUserId.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Name.Contains(search) ||
                (t.Description != null && t.Description.Contains(search)));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var tasks = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TaskListResponse(
                t.Id,
                t.ProjectId,
                t.Project.Name,
                t.Name,
                t.AssignedUser != null ? t.AssignedUser.Name : null,
                t.Priority.ToString(),
                t.DueDate,
                t.Status.ToString(),
                t.Stages.Count,
                t.Stages.Count(s => s.Status == StageStatus.Completed),
                t.Stages.Count == 0 ? 0 :
                    (int)Math.Round((double)t.Stages.Count(s => s.Status == StageStatus.Completed) /
                    t.Stages.Count * 100),
                t.DueDate < today && t.Status != Models.TaskStatus.Completed
            ))
            .ToListAsync();

        return Ok(tasks);
    }

    /// <summary>Get task by ID with stages</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> GetById(Guid id)
    {
        var t = await _db.Tasks
            .Include(t => t.Project)
            .Include(t => t.AssignedUser)
            .Include(t => t.Stages)
                .ThenInclude(s => s.AssignedUser)
            .Include(t => t.Stages)
                .ThenInclude(s => s.StageMaterials)
                .ThenInclude(sm => sm.Material)
            .Include(t => t.Stages)
                .ThenInclude(s => s.Files)
                .ThenInclude(f => f.UploadedBy)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (t is null) return NotFound();

        return Ok(MapToTaskResponse(t));
    }

    /// <summary>Create a new task</summary>
    [HttpPost]
    public async Task<ActionResult<TaskResponse>> Create([FromBody] CreateTaskRequest request)
    {
        var id = request.Id ?? Guid.NewGuid();

        if (await _db.Tasks.AnyAsync(t => t.Id == id))
            return await GetById(id);

        var projectExists = await _db.Projects.AnyAsync(p => p.Id == request.ProjectId);
        if (!projectExists) return BadRequest(new { message = "Проект не найден" });

        if (request.AssignedUserId.HasValue)
        {
            var userExists = await _db.Users.AnyAsync(u => u.Id == request.AssignedUserId.Value);
            if (!userExists) return BadRequest(new { message = "Исполнитель не найден" });
        }

        if (!DueDatePolicy.IsAllowed(request.DueDate))
            return BadRequest(new { message = DueDatePolicy.PastNotAllowedMessage });

        var task = new ProjectTask
        {
            Id = id,
            ProjectId = request.ProjectId,
            Name = request.Name,
            Description = request.Description,
            AssignedUserId = request.AssignedUserId,
            Priority = request.Priority,
            DueDate = request.DueDate,
            Status = Models.TaskStatus.Planned,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        await _log.LogAsync(CurrentUserId(), ActivityActionType.Created,
            ActivityEntityType.Task, task.Id, $"Создана задача: {task.Name}");

        return CreatedAtAction(nameof(GetById), new { id = task.Id },
            await GetById(task.Id));
    }

    /// <summary>Update task</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TaskResponse>> Update(Guid id, [FromBody] UpdateTaskRequest request)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task is null) return NotFound();

        if (!DueDatePolicy.IsAllowed(request.DueDate))
            return BadRequest(new { message = DueDatePolicy.PastNotAllowedMessage });

        var oldStatus = task.Status;

        task.Name = request.Name;
        task.Description = request.Description;
        task.AssignedUserId = request.AssignedUserId;
        task.Priority = request.Priority;
        task.DueDate = request.DueDate;
        task.Status = request.Status;
        task.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var actionType = oldStatus != request.Status
            ? ActivityActionType.StatusChanged
            : ActivityActionType.Updated;

        await _log.LogAsync(CurrentUserId(), actionType,
            ActivityEntityType.Task, task.Id,
            oldStatus != request.Status
                ? $"Статус изменён: {oldStatus} → {request.Status}"
                : $"Обновлена задача: {task.Name}");

        return Ok(await GetById(id));
    }

    /// <summary>Delete task</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var task = await _db.Tasks.FindAsync(id);
        if (task is null) return NotFound();

        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Add dependency between tasks (for Gantt)</summary>
    [HttpPost("{id:guid}/dependencies")]
    public async Task<IActionResult> AddDependency(Guid id, [FromBody] AddTaskDependencyRequest request)
    {
        if (id == request.DependsOnTaskId)
            return BadRequest(new { message = "Задача не может зависеть сама от себя" });

        var taskExists = await _db.Tasks.AnyAsync(t => t.Id == id);
        var depExists = await _db.Tasks.AnyAsync(t => t.Id == request.DependsOnTaskId);

        if (!taskExists || !depExists) return NotFound();

        var alreadyExists = await _db.TaskDependencies
            .AnyAsync(d => d.TaskId == id && d.DependsOnTaskId == request.DependsOnTaskId);

        if (alreadyExists) return Conflict(new { message = "Зависимость уже существует" });

        _db.TaskDependencies.Add(new TaskDependency
        {
            TaskId = id,
            DependsOnTaskId = request.DependsOnTaskId,
            DependencyType = request.DependencyType
        });

        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Remove task dependency</summary>
    [HttpDelete("{id:guid}/dependencies/{dependsOnTaskId:guid}")]
    public async Task<IActionResult> RemoveDependency(Guid id, Guid dependsOnTaskId)
    {
        var dep = await _db.TaskDependencies
            .FirstOrDefaultAsync(d => d.TaskId == id && d.DependsOnTaskId == dependsOnTaskId);

        if (dep is null) return NotFound();

        _db.TaskDependencies.Remove(dep);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static TaskResponse MapToTaskResponse(ProjectTask t)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalStages = t.Stages.Count;
        var completedStages = t.Stages.Count(s => s.Status == StageStatus.Completed);
        var progress = totalStages == 0 ? 0 :
            (int)Math.Round((double)completedStages / totalStages * 100);

        var stages = t.Stages.OrderBy(s => s.CreatedAt).Select(s => new TaskStageResponse(
            s.Id, s.TaskId, s.Name, s.Description,
            s.AssignedUserId,
            s.AssignedUser?.Name,
            s.Status.ToString(),
            s.DueDate,
            s.StageMaterials.Select(sm => new StageMaterialResponse(
                sm.Id, sm.MaterialId, sm.Material.Name, sm.Material.Unit, sm.Quantity)).ToList(),
            s.Files.Select(f => new FileResponse(
                f.Id, f.FileName, f.FileType ?? "", f.FileSize,
                f.UploadedById, f.UploadedBy.Name,
                f.ProjectId, f.TaskId, f.StageId, f.CreatedAt)).ToList(),
            s.CreatedAt, s.UpdatedAt
        )).ToList();

        return new TaskResponse(
            t.Id, t.ProjectId, t.Project.Name, t.Name, t.Description,
            t.AssignedUserId, t.AssignedUser?.Name,
            t.Priority.ToString(), t.DueDate, t.Status.ToString(),
            totalStages, completedStages, progress,
            t.DueDate < today && t.Status != Models.TaskStatus.Completed,
            t.CreatedAt, t.UpdatedAt, stages);
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
