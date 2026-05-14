namespace MPMS.API.Models;

public class StageWorkType
{
    public Guid Id { get; set; }

    public Guid StageId { get; set; }
    public TaskStage Stage { get; set; } = null!;

    public Guid WorkTypeTemplateId { get; set; }
    public WorkTypeTemplate WorkTypeTemplate { get; set; } = null!;

    public string WorkTypeNameSnapshot { get; set; } = string.Empty;
    public string? WorkTypeDescriptionSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }

    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
}
