using System.ComponentModel.DataAnnotations;
using MPMS.API.Models;

namespace MPMS.API.DTOs;

public record CreateTaskRequest(
    Guid ProjectId,
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    TaskPriority Priority,
    DateOnly? DueDate,
    Guid? Id = null
);

public record UpdateTaskRequest(
    [Required, MaxLength(200)] string Name,
    string? Description,
    Guid? AssignedUserId,
    TaskPriority Priority,
    DateOnly? DueDate,
    Models.TaskStatus Status
);

public record TaskResponse(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    string? Description,
    Guid? AssignedUserId,
    string? AssignedUserName,
    string Priority,
    DateOnly? DueDate,
    string Status,
    int TotalStages,
    int CompletedStages,
    int ProgressPercent,
    bool IsOverdue,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<TaskStageResponse>? Stages
);

public record TaskListResponse(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    string? AssignedUserName,
    string Priority,
    DateOnly? DueDate,
    string Status,
    int TotalStages,
    int CompletedStages,
    int ProgressPercent,
    bool IsOverdue
);

public record AddTaskDependencyRequest(
    Guid DependsOnTaskId,
    TaskDependencyType DependencyType
);
