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
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<StageMaterial> StageMaterials => Set<StageMaterial>();
    public DbSet<FileAttachment> Files => Set<FileAttachment>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Role ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // ── User ──────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.HasIndex(e => e.Email).IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Role)
                  .WithMany(r => r.Users)
                  .HasForeignKey(e => e.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Project ───────────────────────────────────────────────────────
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

        // ── ProjectMember ─────────────────────────────────────────────────
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

        // ── ProjectTask ───────────────────────────────────────────────────
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

        // ── TaskStage ─────────────────────────────────────────────────────
        modelBuilder.Entity<TaskStage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Task)
                  .WithMany(t => t.Stages)
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssignedUser)
                  .WithMany(u => u.AssignedStages)
                  .HasForeignKey(e => e.AssignedUserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Material ──────────────────────────────────────────────────────
        modelBuilder.Entity<Material>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // ── StageMaterial ─────────────────────────────────────────────────
        modelBuilder.Entity<StageMaterial>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

            entity.Property(e => e.Quantity).HasPrecision(18, 3);

            entity.HasOne(e => e.Stage)
                  .WithMany(s => s.StageMaterials)
                  .HasForeignKey(e => e.StageId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Material)
                  .WithMany(m => m.StageMaterials)
                  .HasForeignKey(e => e.MaterialId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── FileAttachment ────────────────────────────────────────────────
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

        // ── TaskDependency ────────────────────────────────────────────────
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

        // ── ActivityLog ───────────────────────────────────────────────────
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

        // ── Seed: Roles ───────────────────────────────────────────────────
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000001"), Name = "Administrator",    Description = "Full system access" },
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000002"), Name = "Project Manager",  Description = "Manage projects and tasks" },
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000003"), Name = "Worker",           Description = "View and update assigned tasks" },
            new Role { Id = new Guid("10000000-0000-0000-0000-000000000004"), Name = "Viewer",           Description = "Read-only access" }
        );
    }
}
