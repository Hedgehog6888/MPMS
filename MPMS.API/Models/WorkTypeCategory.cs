using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class WorkTypeCategory
{
    public Guid Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<WorkTypeTemplate> WorkTypes { get; set; } = new List<WorkTypeTemplate>();
}
