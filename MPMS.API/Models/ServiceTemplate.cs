using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class ServiceTemplate
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    [MaxLength(100)]
    public string? Article { get; set; }

    public decimal BasePrice { get; set; }

    public Guid CategoryId { get; set; }
    public ServiceCategory Category { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<TaskStage> TaskStages { get; set; } = new List<TaskStage>();
}
