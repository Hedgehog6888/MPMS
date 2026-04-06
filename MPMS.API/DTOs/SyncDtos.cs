using System.ComponentModel.DataAnnotations;

namespace MPMS.API.DTOs;

public record AssigneeSyncItemDto(Guid Id, Guid UserId);

public record ReplaceTaskAssigneesRequest(List<AssigneeSyncItemDto> Assignees);

public record ReplaceStageAssigneesRequest(List<AssigneeSyncItemDto> Assignees);

public record DiscussionMessageResponse(
    Guid Id,
    Guid? TaskId,
    Guid? ProjectId,
    Guid UserId,
    string UserName,
    string UserInitials,
    string UserColor,
    string UserRole,
    string Text,
    DateTime CreatedAt);

public record CreateDiscussionMessageRequest(
    Guid? Id,
    Guid? TaskId,
    Guid? ProjectId,
    [Required] string Text,
    DateTime? CreatedAt = null);

public record SyncedActivityLogResponse(
    Guid Id,
    Guid? UserId,
    string? ActorRole,
    string UserName,
    string UserInitials,
    string UserColor,
    string? ActionType,
    string ActionText,
    string EntityType,
    Guid EntityId,
    DateTime CreatedAt);

public record CreateSyncedActivityLogRequest(
    Guid Id,
    Guid? UserId,
    string? ActorRole,
    [Required] string UserName,
    string UserInitials,
    string UserColor,
    string? ActionType,
    [Required] string ActionText,
    [Required] string EntityType,
    Guid EntityId,
    DateTime CreatedAt);
