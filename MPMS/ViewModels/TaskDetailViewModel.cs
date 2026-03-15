using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class TaskDetailViewModel : ViewModelBase
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;

    [ObservableProperty] private LocalTask? _task;
    [ObservableProperty] private ObservableCollection<LocalTaskStage> _stages = [];
    [ObservableProperty] private ObservableCollection<LocalStageMaterial> _allMaterials = [];
    [ObservableProperty] private ObservableCollection<LocalStageMaterial> _selectedStageMaterials = [];
    [ObservableProperty] private ObservableCollection<LocalFile> _files = [];
    [ObservableProperty] private ObservableCollection<LocalMessage> _messages = [];
    [ObservableProperty] private LocalTaskStage? _selectedStage;
    [ObservableProperty] private string _activeTab = "Stages";
    [ObservableProperty] private bool _hasNoStages = true;
    [ObservableProperty] private bool _hasNoMaterials = true;
    [ObservableProperty] private bool _hasNoFiles = true;
    [ObservableProperty] private string _stagesTabLabel = "Этапы";

    public TaskDetailViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _sync = sync;
    }

    public void SetTask(LocalTask task) => Task = task;

    public async Task LoadAsync()
    {
        if (Task is null) return;
        await using var db = await _dbFactory.CreateDbContextAsync();

        var stages = await db.TaskStages
            .Where(s => s.TaskId == Task.Id)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        Stages = new ObservableCollection<LocalTaskStage>(stages);
        HasNoStages = stages.Count == 0;
        StagesTabLabel = stages.Count > 0 ? $"Этапы ({stages.Count})" : "Этапы";

        // Load all materials for all stages of this task
        var stageIds = stages.Select(s => s.Id).ToList();
        var mats = await db.StageMaterials
            .Where(sm => stageIds.Contains(sm.StageId))
            .ToListAsync();

        // Populate StageName for display
        foreach (var mat in mats)
        {
            var stage = stages.FirstOrDefault(s => s.Id == mat.StageId);
            mat.StageName = stage?.Name ?? "—";
        }

        AllMaterials = new ObservableCollection<LocalStageMaterial>(mats);
        HasNoMaterials = mats.Count == 0;

        if (SelectedStage is not null)
            await LoadStageMaterialsAsync(SelectedStage.Id);

        // Load files for this task
        var files = await db.Files
            .Where(f => f.TaskId == Task.Id)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
        Files = new ObservableCollection<LocalFile>(files);
        HasNoFiles = files.Count == 0;

        // Refresh task progress from stages (StatusCalculator: завершённый + планируемый = в работе)
        Task.TotalStages = stages.Count;
        Task.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);
        Task.InProgressStages = stages.Count(s => s.Status == StageStatus.InProgress);
        if (stages.Count > 0)
            Task.Status = StatusCalculator.GetTaskStatusFromStages(stages);
        OnPropertyChanged(nameof(Task));

        // Load messages for this task
        var messages = await db.Messages
            .Where(m => m.TaskId == Task.Id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        Messages = new ObservableCollection<LocalMessage>(messages);
    }

    public async Task SendMessageAsync(string text)
    {
        if (Task is null || string.IsNullOrWhiteSpace(text)) return;
        var auth = App.Services.GetRequiredService<IAuthService>();
        await using var db = await _dbFactory.CreateDbContextAsync();

        var userName = auth.UserName ?? "—";
        var initials = string.IsNullOrEmpty(userName) ? "?"
            : string.Concat(userName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpperInvariant() : ""));
        if (string.IsNullOrEmpty(initials)) initials = "?";
        var msg = new LocalMessage
        {
            TaskId = Task.Id,
            UserId = auth.UserId ?? Guid.Empty,
            UserName = userName,
            UserInitials = initials,
            UserColor = "#1B6EC2",
            UserRole = ProjectDetailViewModel.RoleToRussian(auth.UserRole),
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();
        await LogActivityAsync(db, $"Сообщение в задаче «{Task.Name}»", "Message", msg.Id);
        Messages.Add(msg);
    }

    private static async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId)
    {
        var session = await db.AuthSessions.FindAsync(1);
        var userName = session?.UserName ?? "Система";
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : userName.Length > 0 ? $"{userName[0]}" : "?";

        db.ActivityLogs.Add(new LocalActivityLog
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            UserInitials = initials.ToUpper(),
            UserColor = "#1B6EC2",
            ActionText = actionText,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    partial void OnSelectedStageChanged(LocalTaskStage? value)
    {
        if (value is not null)
            _ = LoadStageMaterialsAsync(value.Id);
        else
            SelectedStageMaterials = [];
    }

    private async Task LoadStageMaterialsAsync(Guid stageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var mats = await db.StageMaterials
            .Where(sm => sm.StageId == stageId)
            .ToListAsync();
        SelectedStageMaterials = new ObservableCollection<LocalStageMaterial>(mats);
    }

    public async Task SaveNewStageAsync(CreateStageRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var assignedName = req.AssignedUserId.HasValue
            ? await db.Users.Where(u => u.Id == req.AssignedUserId.Value)
                  .Select(u => u.Name).FirstOrDefaultAsync()
            : null;

        var stage = new LocalTaskStage
        {
            Id = localId,
            TaskId = req.TaskId,
            Name = req.Name,
            Description = req.Description,
            AssignedUserId = req.AssignedUserId,
            AssignedUserName = assignedName,
            Status = StageStatus.Planned,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.TaskStages.Add(stage);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Stage", localId, SyncOperation.Create,
            req with { Id = localId });

        await LoadAsync();
        await UpdateTaskProgressAsync();
    }

    public async Task SaveUpdatedStageAsync(Guid id, UpdateStageRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.FindAsync(id);
        if (stage is null) return;

        stage.Name = req.Name;
        stage.Description = req.Description;
        stage.AssignedUserId = req.AssignedUserId;
        stage.Status = req.Status;
        stage.IsSynced = false;
        stage.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Stage", id, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлён этап «{req.Name}»", "Stage", id);
        await LoadAsync();
        await UpdateTaskProgressAsync();
    }

    public async Task EditTaskAsync(Guid taskId, UpdateTaskRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(taskId);
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
        // Status is auto-calculated from stages (StatusCalculator)
        var stages = await db.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
        task.TotalStages = stages.Count;
        task.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);
        task.InProgressStages = stages.Count(s => s.Status == StageStatus.InProgress);
        if (stages.Count > 0)
            task.Status = StatusCalculator.GetTaskStatusFromStages(stages);
        task.IsSynced = false;
        task.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Task", taskId, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлена задача «{req.Name}»", "Task", taskId);
        await LoadAsync();
    }

    public async Task ChangeTaskStatusAsync(Models.TaskStatus newStatus)
    {
        if (Task is null) return;
        var req = new UpdateTaskRequest(Task.Name, Task.Description, Task.AssignedUserId,
            Task.Priority, Task.DueDate, newStatus);
        await EditTaskAsync(Task.Id, req);
    }

    [RelayCommand]
    private async Task ChangeStageStatusAsync((LocalTaskStage stage, StageStatus newStatus) args)
    {
        var (stage, newStatus) = args;
        var req = new UpdateStageRequest(stage.Name, stage.Description,
            stage.AssignedUserId, newStatus);
        await SaveUpdatedStageAsync(stage.Id, req);
    }

    [RelayCommand]
    private async Task MarkStageForDeletionAsync(LocalTaskStage stage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;

        entity.IsMarkedForDeletion = !entity.IsMarkedForDeletion;
        entity.IsSynced = false;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var action = entity.IsMarkedForDeletion ? "Помечен для удаления" : "Снята пометка удаления";
        await LogActivityAsync(db, $"{action}: этап «{stage.Name}»", "Stage", stage.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteStageAsync(LocalTaskStage stage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;

        db.TaskStages.Remove(entity);
        await db.SaveChangesAsync();

        if (stage.IsSynced)
            await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Delete, new { });

        await LogActivityAsync(db, $"Удалён этап «{stage.Name}»", "Stage", stage.Id);
        await LoadAsync();
        await UpdateTaskProgressAsync();
    }

    private async System.Threading.Tasks.Task UpdateTaskProgressAsync()
    {
        if (Task is null) return;
        var taskId = Task.Id;
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var taskEntity = await ctx.Tasks.FindAsync(taskId);
        if (taskEntity is null) return;
        var stages = await ctx.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
        taskEntity.TotalStages = stages.Count;
        taskEntity.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);

        // Auto-update task status (StatusCalculator)
        if (stages.Count > 0)
            taskEntity.Status = StatusCalculator.GetTaskStatusFromStages(stages);

        taskEntity.IsSynced = false;
        taskEntity.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync();

        // Auto-update project status based on task completion
        await RecalcProjectStatusAsync(ctx, taskEntity.ProjectId);

        // Update the in-memory Task so the UI reflects the new progress immediately
        var inProgressCount = stages.Count(s => s.Status == StageStatus.InProgress);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Task is not null)
            {
                Task.TotalStages = taskEntity.TotalStages;
                Task.CompletedStages = taskEntity.CompletedStages;
                Task.InProgressStages = inProgressCount;
                Task.Status = taskEntity.Status;
                OnPropertyChanged(nameof(Task));
            }
        });
    }

    private static async System.Threading.Tasks.Task RecalcProjectStatusAsync(LocalDbContext db, Guid projectId)
    {
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId && !t.IsMarkedForDeletion).ToListAsync();
        project.Status = StatusCalculator.GetProjectStatusFromTasks(tasks);
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
