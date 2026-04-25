using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class FileAttachment
{
    public Guid Id { get; set; }

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? FilePath { get; set; }

    [MaxLength(100)]
    public string? FileType { get; set; }

    public long FileSize { get; set; }

    public Guid UploadedById { get; set; }
    public User UploadedBy { get; set; } = null!;

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? TaskId { get; set; }
    public ProjectTask? Task { get; set; }

    public Guid? StageId { get; set; }
    public TaskStage? Stage { get; set; }

    public DateTime CreatedAt { get; set; }
    
    public DateTime? OriginalCreatedAt { get; set; }

    /// <summary>Содержимое файла, хранящееся в базе данных (BLOB).</summary>
    public byte[]? Content { get; set; }
}
