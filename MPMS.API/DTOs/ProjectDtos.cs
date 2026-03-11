using System.ComponentModel.DataAnnotations;
using MPMS.API.Models;

namespace MPMS.API.DTOs;

public record CreateProjectRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    [MaxLength(200)] string? Client,
    [MaxLength(500)] string? Address,
    DateOnly? StartDate,
    DateOnly? EndDate,
    Guid ManagerId,
    Guid? Id = null
);

public record UpdateProjectRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    [MaxLength(200)] string? Client,
    [MaxLength(500)] string? Address,
    DateOnly? StartDate,
    DateOnly? EndDate,
    ProjectStatus Status,
    Guid ManagerId
);

public record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string? Client,
    string? Address,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    Guid ManagerId,
    string ManagerName,
    int TotalTasks,
    int CompletedTasks,
    int InProgressTasks,
    int OverdueTasks,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record ProjectListResponse(
    Guid Id,
    string Name,
    string? Client,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string Status,
    string ManagerName
);
