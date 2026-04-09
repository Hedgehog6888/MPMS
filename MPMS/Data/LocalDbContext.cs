using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MPMS.Models;

namespace MPMS.Data;

public class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options) : base(options) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<DateTimeAsUtcConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableDateTimeAsUtcConverter>();
    }

    public DbSet<AuthSession>        AuthSessions     => Set<AuthSession>();
    public DbSet<RecentAccount>      RecentAccounts   => Set<RecentAccount>();
    public DbSet<LocalRole>          Roles            => Set<LocalRole>();
    public DbSet<LocalUser>          Users            => Set<LocalUser>();
    public DbSet<LocalProject>       Projects         => Set<LocalProject>();
    public DbSet<LocalTask>          Tasks            => Set<LocalTask>();
    public DbSet<LocalTaskStage>     TaskStages       => Set<LocalTaskStage>();
    public DbSet<LocalMaterialCategory> MaterialCategories => Set<LocalMaterialCategory>();
    public DbSet<LocalEquipmentCategory> EquipmentCategories => Set<LocalEquipmentCategory>();
    public DbSet<LocalMaterial>      Materials        => Set<LocalMaterial>();
    public DbSet<LocalMaterialStockMovement> MaterialStockMovements => Set<LocalMaterialStockMovement>();
    public DbSet<LocalEquipment>     Equipments       => Set<LocalEquipment>();
    public DbSet<LocalEquipmentHistoryEntry> EquipmentHistoryEntries => Set<LocalEquipmentHistoryEntry>();
    public DbSet<LocalStageMaterial> StageMaterials   => Set<LocalStageMaterial>();
    public DbSet<LocalFile>          Files            => Set<LocalFile>();
    public DbSet<LocalActivityLog>   ActivityLogs     => Set<LocalActivityLog>();
    public DbSet<LocalProjectMember> ProjectMembers    => Set<LocalProjectMember>();
    public DbSet<LocalTaskAssignee>  TaskAssignees     => Set<LocalTaskAssignee>();
    public DbSet<LocalStageAssignee> StageAssignees    => Set<LocalStageAssignee>();
    public DbSet<LocalMessage>       Messages          => Set<LocalMessage>();
    public DbSet<PendingOperation>   PendingOperations => Set<PendingOperation>();
    public DbSet<DeletedUserId>     DeletedUserIds   => Set<DeletedUserId>();

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

        modelBuilder.Entity<LocalMaterial>()
            .Property(e => e.Quantity).HasPrecision(18, 3);
        modelBuilder.Entity<LocalMaterial>()
            .Property(e => e.Cost).HasPrecision(18, 2);
        modelBuilder.Entity<LocalMaterialStockMovement>()
            .Property(e => e.Delta).HasPrecision(18, 3);
        modelBuilder.Entity<LocalMaterialStockMovement>()
            .Property(e => e.QuantityAfter).HasPrecision(18, 3);
    }
}

public class LocalDbContextFactory : IDesignTimeDbContextFactory<LocalDbContext>
{
    public LocalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlite("Data Source=mpms_design.db")
            .Options;
        return new LocalDbContext(options);
    }
}
