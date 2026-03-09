namespace MPMS.API.Models;

/// <summary>
/// Many-to-many: users assigned to projects (besides manager)
/// </summary>
public class ProjectMember
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime JoinedAt { get; set; }
}
