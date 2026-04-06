using System.ComponentModel.DataAnnotations;

namespace MPMS.API.Models;

/// <summary>Сообщения в обсуждении проекта или задачи — синхронизируются с клиентом.</summary>
public class DiscussionMessage
{
    public Guid Id { get; set; }

    public Guid? TaskId { get; set; }
    public ProjectTask? Task { get; set; }

    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    [MaxLength(100)] public string UserName { get; set; } = string.Empty;
    [MaxLength(5)] public string UserInitials { get; set; } = "?";
    [MaxLength(20)] public string UserColor { get; set; } = "#1B6EC2";
    [MaxLength(50)] public string UserRole { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
