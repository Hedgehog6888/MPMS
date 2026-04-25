using AutoMapper;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

/// <summary>Загрузка, скачивание и удаление файлов, связанных с проектами, задачами и этапами.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public FilesController(ApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    /// <summary>Загрузить файл и привязать его к проекту, задаче или этапу.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<FileResponse>> Upload(
        IFormFile file,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? taskId,
        [FromQuery] Guid? stageId,
        [FromQuery] DateTime? originalCreatedAt = null,
        [FromQuery] Guid? id = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Файл не выбран" });

        // if (projectId is null && taskId is null && stageId is null)
        //     return BadRequest(new { message = "Необходимо указать projectId, taskId или stageId" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var fileContent = ms.ToArray();

        try
        {
            var attachment = new FileAttachment
            {
                Id = id ?? Guid.NewGuid(),
                FileName = file.FileName,
                FilePath = null, // Больше не храним на диске
                Content = fileContent,
                FileType = file.ContentType,
                FileSize = file.Length,
                UploadedById = CurrentUserId(),
                ProjectId = projectId,
                TaskId = taskId,
                StageId = stageId,
                CreatedAt = DateTime.UtcNow,
                OriginalCreatedAt = originalCreatedAt
            };

            _db.Files.Add(attachment);
            await _db.SaveChangesAsync();

            await _db.Entry(attachment).Reference(f => f.UploadedBy).LoadAsync();
            if (attachment.UploadedById != Guid.Empty && attachment.UploadedBy == null)
            {
                // Should not happen if FK is correct, but let's be safe
                attachment.UploadedBy = await _db.Users.FindAsync(attachment.UploadedById) ?? new User { FirstName = "System", LastName = "User" };
            }
            
            if (attachment.ProjectId.HasValue) await _db.Entry(attachment).Reference(f => f.Project).LoadAsync();
            if (attachment.StageId.HasValue) await _db.Entry(attachment).Reference(f => f.Stage).LoadAsync();

            var response = _mapper.Map<FileResponse>(attachment);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Ошибка при сохранении файла", detail = ex.Message, inner = ex.InnerException?.Message });
        }
    }

    /// <summary>Скачать файл по идентификатору.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await _db.Files.FindAsync(id);
        if (file is null || file.Content is null) return NotFound();

        return File(file.Content, file.FileType ?? "application/octet-stream", file.FileName);
    }

    /// <summary>Получить список файлов по проекту, задаче или этапу.</summary>
    [HttpGet]
    public async Task<ActionResult<List<FileResponse>>> GetFiles(
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? taskId,
        [FromQuery] Guid? stageId)
    {
        var query = _db.Files
            .Include(f => f.UploadedBy)
            .Include(f => f.Project)
            .Include(f => f.Stage)
            .AsQueryable();

        if (projectId.HasValue) query = query.Where(f => f.ProjectId == projectId);
        if (taskId.HasValue) query = query.Where(f => f.TaskId == taskId);
        if (stageId.HasValue) query = query.Where(f => f.StageId == stageId);

        var files = await query
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        return Ok(_mapper.Map<List<FileResponse>>(files));
    }

    /// <summary>Удалить файл из базы и с диска.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await _db.Files.FindAsync(id);
        if (file is null) return NotFound();

        _db.Files.Remove(file);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
