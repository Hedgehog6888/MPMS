using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class Project
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [MaxLength(200)]
    public string? Client { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;

    public Guid ManagerId { get; set; }
    public User Manager { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public bool IsMarkedForDeletion { get; set; }

    public bool IsArchived { get; set; }

    public ICollection<ProjectTask> Tasks { get; set; } = new List<ProjectTask>();
    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<FileAttachment> Files { get; set; } = new List<FileAttachment>();
}
