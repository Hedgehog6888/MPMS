using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

public class Role
{
    public Guid Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
