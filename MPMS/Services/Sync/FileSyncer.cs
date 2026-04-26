using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using System.Text.Json;
using System.IO;

namespace MPMS.Services.Sync;

public class FileSyncer : IEntitySyncer
{
    private readonly IApiService _api;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileSyncer(IApiService api, JsonSerializerOptions jsonOptions)
    {
        _api = api;
        _jsonOptions = jsonOptions;
    }

    public bool CanHandle(string entityType) => entityType == "File";

    public Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    public async Task PullAsync(LocalDbContext db)
    {
        var files = await _api.GetFilesAsync();
        if (files is null) return;

        var existingFiles = await db.Files.ToDictionaryAsync(f => f.Id);
        foreach (var f in files)
        {
            if (existingFiles.TryGetValue(f.Id, out var local))
            {
                if (local.IsSynced)
                {
                    local.FileName = f.FileName;
                    local.FileType = f.FileType;
                    local.FileSize = f.FileSize;
                    local.UploadedByName = f.UploadedByName;
                    local.ProjectId = f.ProjectId;
                    local.TaskId = f.TaskId;
                    local.StageId = f.StageId;
                    local.CreatedAt = f.CreatedAt;
                    local.OriginalCreatedAt = f.OriginalCreatedAt;
                }
            }
            else
            {
                db.Files.Add(new LocalFile
                {
                    Id = f.Id,
                    FileName = f.FileName,
                    FileType = f.FileType,
                    FileSize = f.FileSize,
                    UploadedById = f.UploadedById,
                    UploadedByName = f.UploadedByName,
                    ProjectId = f.ProjectId,
                    TaskId = f.TaskId,
                    StageId = f.StageId,
                    CreatedAt = f.CreatedAt,
                    OriginalCreatedAt = f.OriginalCreatedAt,
                    IsSynced = true
                });
            }
        }

        var serverFileIds = files.Select(f => f.Id).ToHashSet();
        var orphanFiles = await db.Files
            .Where(f => f.IsSynced && !serverFileIds.Contains(f.Id))
            .ToListAsync();
        db.Files.RemoveRange(orphanFiles);
    }

    public async Task<bool> PushAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteFileAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var meta = JsonSerializer.Deserialize<FileDto>(op.Payload, _jsonOptions);
            if (meta is null) return false;

            var local = await db.Files.FindAsync(op.EntityId);
            if (local is null) return false; 
            if (local.FileData is null || local.FileData.Length == 0) return true; 

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, local.FileName);
            
            await File.WriteAllBytesAsync(tempPath, local.FileData);
            try
            {
                var uploaded = await _api.UploadFileAsync(tempPath, local.ProjectId, local.TaskId, local.StageId, local.OriginalCreatedAt, local.Id);
                if (uploaded is not null)
                {
                    local.IsSynced = true;
                    return true;
                }
                return false;
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        return true;
    }
}
