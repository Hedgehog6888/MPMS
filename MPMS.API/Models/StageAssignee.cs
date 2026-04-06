namespace MPMS.API.Models;

public class StageAssignee
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }
    public TaskStage Stage { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
