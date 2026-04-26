using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services.Sync;

public interface IEntitySyncer
{
    /// <summary>
    /// Determines if this syncer can handle the given entity type for Push operations.
    /// </summary>
    bool CanHandle(string entityType);

    /// <summary>
    /// Performs any necessary preparation before sync operations (e.g., category mapping).
    /// </summary>
    Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    /// <summary>
    /// Pulls data from the server and merges it into the local database.
    /// </summary>
    Task PullAsync(LocalDbContext db);

    /// <summary>
    /// Pushes a single pending operation to the server.
    /// </summary>
    /// <returns>True if success, false otherwise.</returns>
    Task<bool> PushAsync(LocalDbContext db, PendingOperation op);
}
