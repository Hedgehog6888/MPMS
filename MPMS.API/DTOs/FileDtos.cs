namespace MPMS.API.DTOs;

public record FileResponse(
    Guid Id,
    string FileName,
    string FileType,
    long FileSize,
    Guid UploadedById,
    string UploadedByName,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? StageId,
    DateTime CreatedAt,
    DateTime? OriginalCreatedAt = null,
    string? ProjectName = null,
    string? StageName = null
);
