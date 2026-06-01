using AutoMapper;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Сообщения обсуждений для проектов и задач.</summary>
[ApiController]
[Route("api/discussion-messages")]
[Authorize]
public class DiscussionMessagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public DiscussionMessagesController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Получить сообщения обсуждения с фильтрацией по задаче, проекту и дате.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DiscussionMessageResponse>>> GetAll(
        [FromQuery] Guid? taskId,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? stageId,
        [FromQuery] DateTime? since)
    {
        var q = _db.DiscussionMessages.AsQueryable();
        if (taskId.HasValue) q = q.Where(m => m.TaskId == taskId);
        if (projectId.HasValue) q = q.Where(m => m.ProjectId == projectId);
        if (stageId.HasValue) q = q.Where(m => m.StageId == stageId);
        if (since.HasValue) q = q.Where(m => m.CreatedAt > since.Value);

        var list = await q
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        return Ok(_mapper.Map<List<DiscussionMessageResponse>>(list));
    }

    /// <summary>Создать новое сообщение в обсуждении проекта или задачи.</summary>
    [HttpPost]
    public async Task<ActionResult<DiscussionMessageResponse>> Create([FromBody] CreateDiscussionMessageRequest request)
    {
        var taskId = request.TaskId;
        var projectId = request.ProjectId;
        var stageId = request.StageId;
        if (taskId == Guid.Empty) taskId = null;
        if (projectId == Guid.Empty) projectId = null;
        if (stageId == Guid.Empty) stageId = null;
        var hasTask = taskId.HasValue;
        var hasProject = projectId.HasValue;
        var hasStage = stageId.HasValue;

        // Новая логика: разрешены только два варианта контекста:
        // 1) обсуждение проекта (только ProjectId)
        // 2) обсуждение этапа (StageId обязателен, TaskId и ProjectId соответствуют этапу)
        if (hasStage)
        {
            var stage = await _db.TaskStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stageId.Value);
            if (stage is null)
                return NotFound(new { message = "Этап не найден на сервере." });

            // Автозаполним TaskId/ProjectId при необходимости
            if (!hasTask) taskId = stage.TaskId;
            var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId!.Value);
            if (task is null)
                return NotFound(new { message = "Задача этапа не найдена на сервере." });
            if (!hasProject) projectId = task.ProjectId;

            // Проверим согласованность, если они были переданы
            if (hasTask && taskId != stage.TaskId)
                return BadRequest(new { message = "Указанная TaskId не соответствует этапу." });
            if (hasProject && projectId != task.ProjectId)
                return BadRequest(new { message = "Указанная ProjectId не соответствует этапу." });
        }
        else
        {
            // Проектное обсуждение
            if (!hasProject || hasTask)
                return BadRequest(new { message = "Для проектного обсуждения укажите только ProjectId без TaskId/StageId." });
        }

        var id = request.Id ?? Guid.NewGuid();
        if (await _db.DiscussionMessages.AnyAsync(m => m.Id == id))
        {
            var existing = await _db.DiscussionMessages.FirstAsync(m => m.Id == id);
            return Ok(_mapper.Map<DiscussionMessageResponse>(existing));
        }

        if (projectId.HasValue &&
            !await _db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId.Value))
        {
            return NotFound(new
            {
                message = "Проект не найден на сервере (возможно, удалён). Обновите данные или выберите другой проект."
            });
        }

        if (hasStage)
        {
            // уже проверено выше, оставлено для явности
            if (!await _db.TaskStages.AsNoTracking().AnyAsync(s => s.Id == stageId!.Value))
                return NotFound(new { message = "Этап не найден на сервере." });
        }

        var userId = CurrentUserId();
        var user = await _db.Users.Include(u => u.Role).FirstAsync(u => u.Id == userId);
        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        // Время только с сервера: часы на разных ПК и Kind=Unspecified у SQLite не должны ломать порядок ленты.
        var msg = new DiscussionMessage
        {
            Id = id,
            TaskId = taskId,
            ProjectId = projectId,
            StageId = stageId,
            UserId = userId,
            UserName = fullName,
            UserInitials = InitialsFromName(fullName),
            UserColor = "#1B6EC2",
            UserRole = user.Role.Name,
            Text = request.Text.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.DiscussionMessages.Add(msg);
        await _db.SaveChangesAsync();

        return Created($"/api/discussion-messages/{msg.Id}", _mapper.Map<DiscussionMessageResponse>(msg));
    }

    private static string InitialsFromName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
        return name.Length > 0 ? char.ToUpper(name[0]).ToString() : "?";
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
