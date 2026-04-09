namespace MPMS.API.Models;

/// <summary>Дополнительные исполнители задачи (помимо основного AssignedUserId).</summary>
public class TaskAssignee
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public ProjectTask Task { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
