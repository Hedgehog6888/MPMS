using Microsoft.EntityFrameworkCore;
using MPMS.API.Models;

namespace MPMS.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectTask> Tasks => Set<ProjectTask>();
    public DbSet<TaskStage> TaskStages => Set<TaskStage>();
    public DbSet<WorkTypeCategory> WorkTypeCategories => Set<WorkTypeCategory>();
    public DbSet<WorkTypeTemplate> WorkTypeTemplates => Set<WorkTypeTemplate>();
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();
    public DbSet<EquipmentCategory> EquipmentCategories => Set<EquipmentCategory>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialStockMovement> MaterialStockMovements => Set<MaterialStockMovement>();
    public DbSet<Equipment> Equipments => Set<Equipment>();
    public DbSet<EquipmentHistoryEntry> EquipmentHistoryEntries => Set<EquipmentHistoryEntry>();
    public DbSet<StageMaterial> StageMaterials => Set<StageMaterial>();
    public DbSet<StageWorkType> StageWorkTypes => Set<StageWorkType>();
    public DbSet<FileAttachment> Files => Set<FileAttachment>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<StageAssignee> StageAssignees => Set<StageAssignee>();
    public DbSet<DiscussionMessage> DiscussionMessages => Set<DiscussionMessage>();
    public DbSet<SyncedActivityLog> SyncedActivityLogs => Set<SyncedActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Username).IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Role)
                  .WithMany(r => r.Users)
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Manager)
                  .WithMany(u => u.ManagedProjects)
                  .HasForeignKey(e => e.ManagerId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.HasIndex(e => new { e.ProjectId, e.UserId }).IsUnique();

            entity.Property(e => e.JoinedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Project)
                  .WithMany(p => p.Members)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany(u => u.ProjectMemberships)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.ToTable("Tasks");

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.Property(e => e.Priority)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Project)
                  .WithMany(p => p.Tasks)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssignedUser)
                  .WithMany(u => u.AssignedTasks)
                  .HasForeignKey(e => e.AssignedUserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaskStage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.WorkQuantity).HasPrecision(18, 3);
            entity.Property(e => e.WorkPricePerUnit).HasPrecision(18, 2);

            entity.HasOne(e => e.Task)
                  .WithMany(t => t.Stages)
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssignedUser)
                  .WithMany(u => u.AssignedStages)
                  .HasForeignKey(e => e.AssignedUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.WorkTypeTemplate)
                  .WithMany(st => st.TaskStages)
                  .HasForeignKey(e => e.WorkTypeTemplateId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkTypeCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<WorkTypeTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(e => e.BasePrice).HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Article).IsUnique().HasFilter("[Article] IS NOT NULL");

            entity.HasOne(e => e.Category)
                  .WithMany(c => c.WorkTypes)
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MaterialCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<EquipmentCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Material>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.Quantity).HasPrecision(18, 3);
            entity.Property(e => e.Cost).HasPrecision(18, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Category)
                  .WithMany(c => c.Materials)
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MaterialStockMovement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(e => e.Delta).HasPrecision(18, 3);
            entity.Property(e => e.QuantityAfter).HasPrecision(18, 3);
            entity.Property(e => e.OperationType)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Material)
                  .WithMany(m => m.StockMovements)
                  .HasForeignKey(e => e.MaterialId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Project)
                  .WithMany()
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Task)
                  .WithMany()
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Equipment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(e => e.Condition)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Category)
                  .WithMany(c => c.EquipmentItems)
                  .HasForeignKey(e => e.CategoryId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CheckedOutProject)
                  .WithMany()
                  .HasForeignKey(e => e.CheckedOutProjectId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.CheckedOutTask)
                  .WithMany()
                  .HasForeignKey(e => e.CheckedOutTaskId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EquipmentHistoryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(e => e.EventType)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(e => e.PreviousStatus)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(e => e.NewStatus)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(e => e.OccurredAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Equipment)
                  .WithMany(eq => eq.History)
                  .HasForeignKey(e => e.EquipmentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Project)
                  .WithMany()
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Task)
                  .WithMany()
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<StageMaterial>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.Quantity).HasPrecision(18, 3);
            entity.Property(e => e.PricePerUnit).HasPrecision(18, 2);

            entity.HasOne(e => e.Stage)
                  .WithMany(s => s.StageMaterials)
                  .HasForeignKey(e => e.StageId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Material)
                  .WithMany(m => m.StageMaterials)
                  .HasForeignKey(e => e.MaterialId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StageWorkType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(e => e.Quantity).HasPrecision(18, 3);
            entity.Property(e => e.PricePerUnit).HasPrecision(18, 2);

            entity.HasOne(e => e.Stage)
                  .WithMany(s => s.StageWorkTypes)
                  .HasForeignKey(e => e.StageId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.WorkTypeTemplate)
                  .WithMany()
                  .HasForeignKey(e => e.WorkTypeTemplateId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FileAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.UploadedBy)
                  .WithMany(u => u.UploadedFiles)
                  .HasForeignKey(e => e.UploadedById)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Project)
                  .WithMany(p => p.Files)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Task)
                  .WithMany(t => t.Files)
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Stage)
                  .WithMany(s => s.Files)
                  .HasForeignKey(e => e.StageId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TaskDependency>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.DependencyType)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.HasIndex(e => new { e.TaskId, e.DependsOnTaskId }).IsUnique();

            entity.HasOne(e => e.Task)
                  .WithMany(t => t.Dependencies)
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.DependsOnTask)
                  .WithMany(t => t.Dependents)
                  .HasForeignKey(e => e.DependsOnTaskId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.ActionType)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.Property(e => e.EntityType)
                  .HasConversion<string>()
                  .HasMaxLength(30);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.ActivityLogs)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskAssignee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.TaskId, e.UserId }).IsUnique();

            entity.HasOne(e => e.Task)
                  .WithMany(t => t.TaskAssignees)
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StageAssignee>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => new { e.StageId, e.UserId }).IsUnique();

            entity.HasOne(e => e.Stage)
                  .WithMany(s => s.StageAssignees)
                  .HasForeignKey(e => e.StageId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DiscussionMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Task)
                  .WithMany()
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Project)
                  .WithMany()
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SyncedActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000001"), Name = "Administrator",   Description = "Полный доступ к системе" },
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000002"), Name = "Project Manager", Description = "Управление проектами и задачами" },
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000003"), Name = "Foreman",         Description = "Управление этапами, назначение работников" },
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000004"), Name = "Worker",          Description = "Просмотр и обновление назначенных задач и этапов" }
        );
    }
}
