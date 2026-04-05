using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class MaterialStockMovement
{
    public Guid Id { get; set; }

    public Guid MaterialId { get; set; }
    public Material Material { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    public decimal Delta { get; set; }

    public decimal QuantityAfter { get; set; }

    public MaterialStockOperationType OperationType { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid? TaskId { get; set; }
    public ProjectTask? Task { get; set; }
}
