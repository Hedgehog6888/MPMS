namespace MPMS.API.Models;

public class WorkTypePriceHistory
{
    public Guid Id { get; set; }
    public Guid WorkTypeId { get; set; }
    public WorkTypeTemplate WorkType { get; set; } = null!;
    
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ChangedAt { get; set; }
}
