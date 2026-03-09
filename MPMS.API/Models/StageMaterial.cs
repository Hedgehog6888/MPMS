namespace MPMS.API.Models;

public class StageMaterial
{
    public Guid Id { get; set; }

    public Guid StageId { get; set; }
    public TaskStage Stage { get; set; } = null!;

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public decimal Quantity { get; set; }
}
