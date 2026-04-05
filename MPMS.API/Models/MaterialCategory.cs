using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class MaterialCategory
{
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public ICollection<Material> Materials { get; set; } = new List<Material>();
}
