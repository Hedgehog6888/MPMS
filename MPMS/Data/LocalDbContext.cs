using Microsoft.EntityFrameworkCore;
using MPMS.Models;

namespace MPMS.Data;

public class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options) : base(options) { }

    public DbSet<AuthSession>        AuthSessions     => Set<AuthSession>();
    public DbSet<RecentAccount>      RecentAccounts   => Set<RecentAccount>();
    public DbSet<LocalRole>          Roles            => Set<LocalRole>();
    public DbSet<LocalUser>          Users            => Set<LocalUser>();
    public DbSet<LocalProject>       Projects         => Set<LocalProject>();
    public DbSet<LocalTask>          Tasks            => Set<LocalTask>();
    public DbSet<LocalTaskStage>     TaskStages       => Set<LocalTaskStage>();
    public DbSet<LocalMaterial>      Materials        => Set<LocalMaterial>();
    public DbSet<LocalStageMaterial> StageMaterials   => Set<LocalStageMaterial>();
    public DbSet<LocalFile>          Files            => Set<LocalFile>();
    public DbSet<PendingOperation>   PendingOperations => Set<PendingOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enum → string conversions (SQLite stores as text)
        modelBuilder.Entity<LocalProject>()
            .Property(e => e.Status).HasConversion<string>();

        modelBuilder.Entity<LocalTask>()
            .Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<LocalTask>()
            .Property(e => e.Priority).HasConversion<string>();

        modelBuilder.Entity<LocalTaskStage>()
            .Property(e => e.Status).HasConversion<string>();

        modelBuilder.Entity<PendingOperation>()
            .Property(e => e.OperationType).HasConversion<string>();

        // AuthSession is a singleton row — always Id = 1
        // UserName (display) and Username (login) differ only by case → SQLite sees duplicates.
        // Map UserName to a distinct column name "UserDisplayName".
        modelBuilder.Entity<AuthSession>()
            .HasKey(e => e.Id);
        modelBuilder.Entity<AuthSession>()
            .Property(e => e.UserName)
            .HasColumnName("UserDisplayName");
    }
}
