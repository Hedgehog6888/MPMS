using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

public partial class ProjectDetailViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private Action? _goBackAction;

    [ObservableProperty] private LocalProject? _project;
    [ObservableProperty] private ObservableCollection<LocalTask> _tasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _plannedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _inProgressTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _pausedTasks = [];
    [ObservableProperty] private ObservableCollection<LocalTask> _completedTasks = [];
    [ObservableProperty] private string _activeTab = "Tasks";
    [ObservableProperty] private string _taskViewMode = "List";
    [ObservableProperty] private int _totalTasks;
    [ObservableProperty] private int _completedTasksCount;
    [ObservableProperty] private int _inProgressTasksCount;
    [ObservableProperty] private int _overdueTasksCount;

    public ProjectDetailViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _sync = sync;
    }

    public void SetProject(LocalProject project, Action? goBackAction = null)
    {
        Project = project;
        _goBackAction = goBackAction;
    }

    public async Task LoadAsync()
    {
        if (Project is null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == Project.Id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        Tasks = new ObservableCollection<LocalTask>(tasks);
        PlannedTasks    = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.Planned));
        InProgressTasks = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.InProgress));
        PausedTasks     = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.Paused));
        CompletedTasks  = new ObservableCollection<LocalTask>(tasks.Where(t => t.Status == TaskStatus.Completed));

        TotalTasks = tasks.Count;
        CompletedTasksCount = tasks.Count(t => t.Status == TaskStatus.Completed);
        InProgressTasksCount = tasks.Count(t => t.Status == TaskStatus.InProgress);
        OverdueTasksCount = tasks.Count(t => t.IsOverdue);
    }

    [RelayCommand]
    private void GoBack() => _goBackAction?.Invoke();

    [RelayCommand]
    private void SwitchTab(string tab) => ActiveTab = tab;

    [RelayCommand]
    private void SwitchTaskView(string mode) => TaskViewMode = mode;

    public async Task SaveNewTaskAsync(CreateTaskRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        var task = new LocalTask
        {
            Id = localId,
            ProjectId = req.ProjectId,
            ProjectName = Project?.Name ?? "—",
            Name = req.Name,
            Description = req.Description,
            AssignedUserId = req.AssignedUserId,
            AssignedUserName = assignedName,
            Priority = req.Priority,
            DueDate = req.DueDate,
            Status = TaskStatus.Planned,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Task", localId, SyncOperation.Create,
            req with { Id = localId });

        await LoadAsync();
    }

    public async Task SaveUpdatedTaskAsync(Guid id, UpdateTaskRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(id);
        if (task is null) return;

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        task.Name = req.Name;
        task.Description = req.Description;
        task.AssignedUserId = req.AssignedUserId;
        task.AssignedUserName = assignedName;
        task.Priority = req.Priority;
        task.DueDate = req.DueDate;
        task.Status = req.Status;
        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Task", id, SyncOperation.Update, req);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task MoveTaskAsync((LocalTask task, Models.TaskStatus newStatus) args)
    {
        var (task, newStatus) = args;
        var req = new UpdateTaskRequest(task.Name, task.Description, task.AssignedUserId,
            task.Priority, task.DueDate, newStatus);
        await SaveUpdatedTaskAsync(task.Id, req);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(LocalTask task)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Tasks.FindAsync(task.Id);
        if (entity is null) return;

        db.Tasks.Remove(entity);
        await db.SaveChangesAsync();

        if (task.IsSynced)
            await _sync.QueueOperationAsync("Task", task.Id, SyncOperation.Delete, new { });

        await LoadAsync();
    }
}
