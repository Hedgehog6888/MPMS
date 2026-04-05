using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class EquipmentCategory
{
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public ICollection<Equipment> EquipmentItems { get; set; } = new List<Equipment>();
}
