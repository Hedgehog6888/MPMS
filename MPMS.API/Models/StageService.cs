namespace MPMS.API.Models;

public class StageService
{
    public Guid Id { get; set; }

    public Guid StageId { get; set; }
    public TaskStage Stage { get; set; } = null!;

    public Guid ServiceTemplateId { get; set; }
    public ServiceTemplate ServiceTemplate { get; set; } = null!;

    public string ServiceNameSnapshot { get; set; } = string.Empty;
    public string? ServiceDescriptionSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }

    public decimal Quantity { get; set; }
    public decimal PricePerUnit { get; set; }
}
