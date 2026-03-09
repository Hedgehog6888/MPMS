namespace MPMS.API.Models;

public enum ProjectStatus
{
    Planning,
    InProgress,
    Completed,
    Cancelled
}

public enum TaskStatus
{
    Planned,
    InProgress,
    Paused,
    Completed
}

public enum StageStatus
{
    Planned,
    InProgress,
    Completed
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum TaskDependencyType
{
    FinishToStart,
    StartToStart,
    FinishToFinish
}

public enum ActivityActionType
{
    Created,
    Updated,
    Deleted,
    StatusChanged,
    FileUploaded,
    AssigneeChanged
}

public enum ActivityEntityType
{
    Project,
    Task,
    TaskStage,
    Material,
    File,
    User
}
