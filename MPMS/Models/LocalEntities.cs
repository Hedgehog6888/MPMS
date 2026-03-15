using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MPMS.Services;

namespace MPMS.Models;

/// <summary>Base for all local entities — tracks offline sync state</summary>
public abstract class LocalEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsSynced { get; set; } = false;
    public DateTime LastModifiedLocally { get; set; } = DateTime.UtcNow;
}

public class LocalRole
{
    public Guid Id { get; set; }
    [MaxLength(50)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class LocalUser : LocalEntity
{
    [MaxLength(100)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)]  public string Username { get; set; } = string.Empty;
    [MaxLength(255)] public string? Email { get; set; }
    public Guid RoleId { get; set; }
    [MaxLength(50)]  public string RoleName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    [MaxLength(500)] public string? AvatarPath { get; set; }
}

public class LocalProject : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [MaxLength(200)] public string? Client { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    public Guid ManagerId { get; set; }
    [MaxLength(100)] public string ManagerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;

    [NotMapped] public int TotalTasks { get; set; }
    [NotMapped] public int CompletedTasks { get; set; }
    [NotMapped] public int InProgressTasks { get; set; }
    /// <summary>ProgressCalculator: целые % (37, 83), зависимость от распределения</summary>
    [NotMapped] public int ProgressPercent => (int)Math.Round(ProgressCalculator.GetProjectProgressPercent(
        CompletedTasks, InProgressTasks, TotalTasks));
    [NotMapped] public string ManagerInitials => string.IsNullOrWhiteSpace(ManagerName) ? "?" :
        string.Join("", ManagerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(w => w[0].ToString().ToUpper()));
}

public class LocalTask : LocalEntity
{
    public Guid ProjectId { get; set; }
    [MaxLength(200)] public string ProjectName { get; set; } = string.Empty;
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? AssignedUserId { get; set; }
    [MaxLength(100)] public string? AssignedUserName { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateOnly? DueDate { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Planned;
    public int TotalStages { get; set; }
    public int CompletedStages { get; set; }
    [NotMapped] public int InProgressStages { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;

    /// <summary>ProgressCalculator: целые % (37, 83), зависимость от распределения</summary>
    public int ProgressPercent => (int)Math.Round(ProgressCalculator.GetTaskProgressPercent(
        CompletedStages, InProgressStages, TotalStages));

    public bool IsOverdue => DueDate.HasValue
        && DueDate < DateOnly.FromDateTime(DateTime.Today)
        && Status != TaskStatus.Completed;
}

public class LocalTaskStage : LocalEntity
{
    public Guid TaskId { get; set; }
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? AssignedUserId { get; set; }
    [MaxLength(100)] public string? AssignedUserName { get; set; }
    public StageStatus Status { get; set; } = StageStatus.Planned;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsMarkedForDeletion { get; set; } = false;
    [NotMapped] public string TaskName { get; set; } = string.Empty;
}

public class LocalMaterial : LocalEntity
{
    [MaxLength(200)] public string Name { get; set; } = string.Empty;
    [MaxLength(50)]  public string? Unit { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LocalStageMaterial : LocalEntity
{
    public Guid StageId { get; set; }
    public Guid MaterialId { get; set; }
    [MaxLength(200)] public string MaterialName { get; set; } = string.Empty;
    [MaxLength(50)]  public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    [NotMapped] public string StageName { get; set; } = string.Empty;
}

public class LocalFile : LocalEntity
{
    [MaxLength(255)]  public string FileName { get; set; } = string.Empty;
    [MaxLength(1000)] public string FilePath { get; set; } = string.Empty;
    [MaxLength(100)]  public string? FileType { get; set; }
    public long FileSize { get; set; }
    public Guid UploadedById { get; set; }
    [MaxLength(100)] public string UploadedByName { get; set; } = string.Empty;
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? StageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Project member — users assigned to a project (executors).</summary>
public class LocalProjectMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(50)] public string UserRole { get; set; } = string.Empty;

    /// <summary>Avatar path from LocalUser — populated when loading members.</summary>
    [NotMapped] public string? AvatarPath { get; set; }

    [NotMapped]
    public string Initials => string.IsNullOrWhiteSpace(UserName) ? "?"
        : string.Join("", UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpper() : ""));
}

/// <summary>Task assignee — supports multiple assignees per task.</summary>
public class LocalTaskAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;

    /// <summary>Avatar path from LocalUser — populated when loading.</summary>
    [NotMapped] public string? AvatarPath { get; set; }
}

/// <summary>Stage assignee — only users from parent task's assignees.</summary>
public class LocalStageAssignee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StageId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;

    /// <summary>Avatar path from LocalUser — populated when loading.</summary>
    [NotMapped] public string? AvatarPath { get; set; }
}

/// <summary>Message/comment on a task or project.</summary>
public class LocalMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TaskId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)] public string UserInitials { get; set; } = "?";
    [MaxLength(20)] public string UserColor { get; set; } = "#1B6EC2";
    [MaxLength(50)] public string UserRole { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Local activity log entry — tracks user actions for the activity feed.</summary>
public class LocalActivityLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)]   public string UserInitials { get; set; } = "?";
    [MaxLength(20)]  public string UserColor { get; set; } = "#1B6EC2";
    [MaxLength(500)] public string ActionText { get; set; } = string.Empty;
    [MaxLength(50)]  public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Stores the JWT token and current user info between sessions</summary>
public class AuthSession
{
    public int Id { get; set; } = 1;
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string ApiBaseUrl { get; set; } = "http://localhost:5147/";

    /// <summary>BCrypt hash of the last entered password — allows offline login.</summary>
    public string LocalPasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// True while the user is actively logged in.
    /// Set to false on logout (record is kept so offline re-login to the same account works).
    /// </summary>
    public bool IsActiveSession { get; set; } = true;
}

/// <summary>Stores the last N accounts that logged in — shown in the login window</summary>
public class RecentAccount
{
    public int Id { get; set; }
    [MaxLength(50)]  public string Username { get; set; } = string.Empty;
    [MaxLength(100)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(50)]  public string Role { get; set; } = string.Empty;
    [MaxLength(20)]  public string AvatarColor { get; set; } = "#1B6EC2";
    [MaxLength(5)]   public string Initials { get; set; } = "?";
    public DateTime LastLoginAt { get; set; }

    /// <summary>Derive initials and color from name and role</summary>
    public static RecentAccount From(string username, string displayName, string role)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : displayName.Length > 0 ? $"{displayName[0]}" : "?";

        var color = role switch
        {
            "Administrator"   => "#C0392B",
            "Project Manager" => "#2980B9",
            "Foreman"         => "#27AE60",
            "Worker"          => "#E67E22",
            _                 => "#1B6EC2"
        };

        return new RecentAccount
        {
            Username = username,
            DisplayName = displayName,
            Role = role,
            AvatarColor = color,
            Initials = initials.ToUpper(),
            LastLoginAt = DateTime.UtcNow
        };
    }
}
