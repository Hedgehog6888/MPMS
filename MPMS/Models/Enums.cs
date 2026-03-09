namespace MPMS.Models;

public enum ProjectStatus  { Planning, InProgress, Completed, Cancelled }
public enum TaskStatus     { Planned, InProgress, Paused, Completed }
public enum StageStatus    { Planned, InProgress, Completed }
public enum TaskPriority   { Low, Medium, High, Critical }
public enum SyncOperation  { Create, Update, Delete }
