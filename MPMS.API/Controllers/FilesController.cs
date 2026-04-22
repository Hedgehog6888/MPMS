using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MPMS.API.Data;
using MPMS.API.DTOs;
using MPMS.API.Models;

namespace MPMS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public FilesController(ApplicationDbContext db, IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _config = config;
        _env = env;
    }

    /// <summary>Upload file (link to project, task, or stage via query params)</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)] // 50 MB
    public async Task<ActionResult<FileResponse>> Upload(
        IFormFile file,
        [FromQuery] Guid? projectId,
        [FromQuery] Guid? taskId,
        [FromQuery] Guid? stageId,
        [FromQuery] DateTime? originalCreatedAt = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Файл не выбран" });

        if (projectId is null && taskId is null && stageId is null)
            return BadRequest(new { message = "Необходимо указать projectId, taskId или stageId" });

        var basePath = Path.Combine(_env.ContentRootPath,
            _config["FileStorage:BasePath"] ?? "wwwroot/uploads");
        Directory.CreateDirectory(basePath);

        var ext = Path.GetExtension(file.FileName);
        var savedName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(basePath, savedName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        var attachment = new FileAttachment
        {
            FileName = file.FileName,
            FilePath = savedName,
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

        return Ok(new FileResponse(
            attachment.Id, attachment.FileName, attachment.FileType ?? "",
            attachment.FileSize, attachment.UploadedById,
            attachment.UploadedBy.Name,
            attachment.ProjectId, attachment.TaskId, attachment.StageId,
            attachment.CreatedAt, attachment.OriginalCreatedAt));
    }

    /// <summary>Download file by ID</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id)
    {
        var file = await _db.Files.FindAsync(id);
        if (file is null) return NotFound();

        var basePath = Path.Combine(_env.ContentRootPath,
            _config["FileStorage:BasePath"] ?? "wwwroot/uploads");
        var fullPath = Path.Combine(basePath, file.FilePath);

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "Файл не найден на диске" });

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
        return File(bytes, file.FileType ?? "application/octet-stream", file.FileName);
    }

    /// <summary>Get files by project / task / stage</summary>
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
            .Select(f => new FileResponse(
                f.Id, f.FileName, f.FileType ?? "",
                f.FileSize, f.UploadedById, f.UploadedBy.Name,
                f.ProjectId, f.TaskId, f.StageId, f.CreatedAt,
                f.OriginalCreatedAt, f.Project != null ? f.Project.Name : null, f.Stage != null ? f.Stage.Name : null))
            .ToListAsync();

        return Ok(files);
    }

    /// <summary>Delete file</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await _db.Files.FindAsync(id);
        if (file is null) return NotFound();

        var basePath = Path.Combine(_env.ContentRootPath,
            _config["FileStorage:BasePath"] ?? "wwwroot/uploads");
        var fullPath = Path.Combine(basePath, file.FilePath);

        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);

        _db.Files.Remove(file);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
