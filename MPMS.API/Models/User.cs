using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MPMS.API.Models;

public class User
{
    public Guid Id { get; set; }

    [Required, MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Computed display name — FirstName + LastName.</summary>
    [NotMapped]
    public string Name => $"{FirstName} {LastName}".Trim();

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Email { get; set; }

    public DateOnly? BirthDate { get; set; }

    [MaxLength(500)]
    public string? HomeAddress { get; set; }

    [Required, MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    public Guid RoleId { get; set; }
    public Role Role { get; set; } = null!;

    /// <summary>Primary worker specialty (e.g. "Электромонтажник").</summary>
    [MaxLength(100)]
    public string? SubRole { get; set; }

    /// <summary>JSON array of additional specialties.</summary>
    public string? AdditionalSubRoles { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Avatar stored as PNG bytes.</summary>
    public byte[]? AvatarData { get; set; }

    public bool IsBlocked { get; set; }

    public DateTime? BlockedAt { get; set; }

    [MaxLength(500)]
    public string? BlockedReason { get; set; }

    public ICollection<Project> ManagedProjects { get; set; } = new List<Project>();
    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public ICollection<ProjectTask> AssignedTasks { get; set; } = new List<ProjectTask>();
    public ICollection<TaskStage> AssignedStages { get; set; } = new List<TaskStage>();
    public ICollection<FileAttachment> UploadedFiles { get; set; } = new List<FileAttachment>();
    public ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();
}
