using System.ComponentModel.DataAnnotations;

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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int ProgressPercent => TotalStages == 0 ? 0
        : (int)Math.Round((double)CompletedStages / TotalStages * 100);

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
