using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services.Sync;

public interface IEntitySyncer
{
    bool CanHandle(string entityType);

    Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    Task PullAsync(LocalDbContext db);

    Task<bool> PushAsync(LocalDbContext db, PendingOperation op);
}
