using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.IO;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services.Sync;

namespace MPMS.Services;

public interface ISyncService
{
    bool IsSyncing { get; }
    bool IsOnline  { get; }
    event EventHandler<bool>? OnlineStatusChanged;
    DateTime? LastSyncTime { get; }
    Task SyncAsync();
    Task QueueOperationAsync(string entityType, Guid entityId,
        SyncOperation operation, object payload);

    Task QueueLocalActivityLogAsync(LocalActivityLog log);
}

public class SyncCoordinator : ISyncService
{
    private static readonly JsonSerializerOptions PendingOpJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private readonly IEnumerable<IEntitySyncer> _syncers;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromMinutes(5));
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private bool _isSyncing;

    private DateTime? _lastSyncTime;

    public bool IsSyncing => _isSyncing;
    public bool IsOnline  => _api.IsOnline;
    public DateTime? LastSyncTime => _lastSyncTime;
    public event EventHandler<bool>? OnlineStatusChanged;

    public SyncCoordinator(
        IDbContextFactory<LocalDbContext> dbFactory,
        IApiService api, 
        IAuthService auth,
        IEnumerable<IEntitySyncer> syncers)
    {
        _dbFactory = dbFactory;
        _api = api;
        _auth = auth;
        _syncers = syncers;
        _ = RunPeriodicSyncAsync();
    }

    private async Task PrepareSyncConnectionAsync()
    {
        _api.ClearLastUsersPullError();
        await _api.ProbeAsync();
        if (_api.IsOnline)
            await _auth.TryRefreshJwtIfNeededAsync(_api);
    }

    public async Task SyncAsync()
    {
        await PrepareSyncConnectionAsync();

        if (!_auth.IsAuthenticated)
        {
            OnlineStatusChanged?.Invoke(this, _api.IsOnline);
            return;
        }

        await _syncGate.WaitAsync();
        try
        {
            _isSyncing = true;
            // Connection already prepared above

            await using (var dbInit = await _dbFactory.CreateDbContextAsync())
            {
                await dbInit.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            }
            
            // 1. Send local changes to server
            try
            {
                await ProcessPendingOperationsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] ProcessPendingOperations: {ex}");
            }

            if (!_api.IsOnline) return;

            // 2. Pull latest data from server
            await PullFromServerAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sync] SyncAsync Error: {ex}");
        }
        finally
        {
            _isSyncing = false;
            if (_api.IsOnline)
            {
                _lastSyncTime = DateTime.Now;
            }
            OnlineStatusChanged?.Invoke(this, _api.IsOnline);
            _syncGate.Release();
        }
    }

    public async Task QueueOperationAsync(string entityType, Guid entityId,
        SyncOperation operation, object payload)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.PendingOperations.Add(new PendingOperation
        {
            EntityType = entityType,
            EntityId = entityId,
            OperationType = operation,
            Payload = JsonSerializer.Serialize(payload, PendingOpJson),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        _ = SyncAsync();
    }

    public Task QueueLocalActivityLogAsync(LocalActivityLog log)
    {
        var dto = new CreateSyncedActivityLogRequest(
            log.Id, log.UserId, log.ActorRole,
            log.UserName, log.UserInitials, log.UserColor,
            log.ActionType, log.ActionText, log.EntityType, log.EntityId, log.CreatedAt);
        return QueueOperationAsync("SyncedActivityLog", log.Id, SyncOperation.Create, dto);
    }

    private async Task PullFromServerAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var syncer in _syncers)
            {
                if (!_api.IsOnline) break;
                await syncer.PullAsync(db);
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Debug.WriteLine($"[Sync] PullFromServer transaction rolled back: {ex.Message}");
            throw;
        }
    }

    private async Task ProcessPendingOperationsAsync()
    {
        if (!_api.IsOnline) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Preparation phase (e.g. warehouse category remapping)
        foreach (var syncer in _syncers)
        {
            try
            {
                await syncer.PrepareAsync(db);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Sync] {syncer.GetType().Name}.PrepareAsync: {ex}");
            }
        }

        await PushOrphanedFilesAsync(db);

        var pending = await db.PendingOperations
            .Where(p => !p.IsFailed)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();

        int consecutiveNetworkErrors = 0;
        foreach (var op in pending)
        {
            if (!_api.IsOnline) break;
            if (consecutiveNetworkErrors >= 3) break;

            try
            {
                var syncer = _syncers.FirstOrDefault(s => s.CanHandle(op.EntityType));
                bool success = false;
                
                if (syncer != null)
                {
                    success = await syncer.PushAsync(db, op);
                }
                else
                {
                    // If no syncer handles this, we might consider it a success or a permanent failure.
                    // For now, let's treat it as handled to avoid blocking the queue.
                    success = true;
                }

                if (success)
                {
                    db.PendingOperations.Remove(op);
                    consecutiveNetworkErrors = 0;
                }
                else
                {
                    op.RetryCount++;
                    if (op.RetryCount >= 5) op.IsFailed = true;
                    consecutiveNetworkErrors++;
                    await Task.Delay(1000); 
                }
                
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                op.ErrorMessage = ex.Message;
                op.RetryCount++;
                if (op.RetryCount >= 10) op.IsFailed = true;
                Debug.WriteLine($"[Sync] Operation {op.Id} failed: {ex.Message}");
                await db.SaveChangesAsync();
            }
        }
    }

    private async Task PushOrphanedFilesAsync(LocalDbContext db)
    {
        var unsyncedFileIds = await db.Files
            .Where(f => !f.IsSynced)
            .Select(f => f.Id)
            .ToListAsync();

        if (!unsyncedFileIds.Any()) return;

        var pendingFileIds = await db.PendingOperations
            .Where(op => op.EntityType == "File" && op.OperationType == SyncOperation.Create)
            .Select(op => op.EntityId)
            .ToListAsync();

        var orphanedIds = unsyncedFileIds.Except(pendingFileIds).ToList();

        foreach (var id in orphanedIds)
        {
            var file = await db.Files.FindAsync(id);
            if (file == null) continue;

            var dto = new FileDto(file.Id, file.FileName, file.FileType ?? "", file.FileSize,
                file.UploadedById, file.UploadedByName, file.ProjectId, file.TaskId, file.StageId,
                file.CreatedAt, file.OriginalCreatedAt);

            var payload = JsonSerializer.Serialize(dto, PendingOpJson);
            db.PendingOperations.Add(new PendingOperation
            {
                Id = Guid.NewGuid(),
                EntityType = "File",
                EntityId = id,
                OperationType = SyncOperation.Create,
                Payload = payload,
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task RunPeriodicSyncAsync()
    {
        while (await _timer.WaitForNextTickAsync())
            await SyncAsync();
    }
}
