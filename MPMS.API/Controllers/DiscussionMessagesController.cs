using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/discussion-messages")]
[Authorize]
public class DiscussionMessagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public DiscussionMessagesController(ApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<DiscussionMessageResponse>>> GetAll(
        [FromQuery] Guid? taskId,
        [FromQuery] Guid? projectId,
        [FromQuery] DateTime? since)
    {
        var q = _db.DiscussionMessages.AsQueryable();
        if (taskId.HasValue) q = q.Where(m => m.TaskId == taskId);
        if (projectId.HasValue) q = q.Where(m => m.ProjectId == projectId);
        if (since.HasValue) q = q.Where(m => m.CreatedAt > since.Value);

        var list = await q
            .OrderBy(m => m.CreatedAt)
            .Select(m => new DiscussionMessageResponse(
                m.Id, m.TaskId, m.ProjectId, m.UserId, m.UserName, m.UserInitials,
                m.UserColor, m.UserRole, m.Text, m.CreatedAt))
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<DiscussionMessageResponse>> Create([FromBody] CreateDiscussionMessageRequest request)
    {
        var hasTask = request.TaskId.HasValue;
        var hasProject = request.ProjectId.HasValue;
        if (hasTask == hasProject)
            return BadRequest(new { message = "Укажите ровно одно из: TaskId или ProjectId" });

        var id = request.Id ?? Guid.NewGuid();
        if (await _db.DiscussionMessages.AnyAsync(m => m.Id == id))
        {
            var existing = await _db.DiscussionMessages.FirstAsync(m => m.Id == id);
            return Ok(new DiscussionMessageResponse(
                existing.Id, existing.TaskId, existing.ProjectId, existing.UserId,
                existing.UserName, existing.UserInitials, existing.UserColor, existing.UserRole,
                existing.Text, existing.CreatedAt));
        }

        var userId = CurrentUserId();
        var user = await _db.Users.Include(u => u.Role).FirstAsync(u => u.Id == userId);
        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        var msg = new DiscussionMessage
        {
            Id = id,
            TaskId = request.TaskId,
            ProjectId = request.ProjectId,
            UserId = userId,
            UserName = fullName,
            UserInitials = InitialsFromName(fullName),
            UserColor = "#1B6EC2",
            UserRole = user.Role.Name,
            Text = request.Text.Trim(),
            CreatedAt = request.CreatedAt ?? DateTime.UtcNow
        };

        _db.DiscussionMessages.Add(msg);
        await _db.SaveChangesAsync();

        return Created($"/api/discussion-messages/{msg.Id}", new DiscussionMessageResponse(
            msg.Id, msg.TaskId, msg.ProjectId, msg.UserId, msg.UserName, msg.UserInitials,
            msg.UserColor, msg.UserRole, msg.Text, msg.CreatedAt));
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
