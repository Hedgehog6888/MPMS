using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class Material
{
    public Guid Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Unit { get; set; }

    public string? Description { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Цена за единицу (учёт).</summary>
    public decimal? Cost { get; set; }

    [MaxLength(100)]
    public string? InventoryNumber { get; set; }

    public Guid? CategoryId { get; set; }
    public MaterialCategory? Category { get; set; }

    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsWrittenOff { get; set; }

    public DateTime? WrittenOffAt { get; set; }

    [MaxLength(500)]
    public string? WrittenOffComment { get; set; }

    public ICollection<StageMaterial> StageMaterials { get; set; } = new List<StageMaterial>();

    public ICollection<MaterialStockMovement> StockMovements { get; set; } = new List<MaterialStockMovement>();
}
