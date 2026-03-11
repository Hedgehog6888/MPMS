using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
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
    [ObservableProperty] private ObservableCollection<LocalStageMaterial> _selectedStageMaterials = [];
    [ObservableProperty] private LocalTaskStage? _selectedStage;

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

        if (SelectedStage is not null)
            await LoadStageMaterialsAsync(SelectedStage.Id);
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
        UpdateTaskProgress(db);
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
        await LoadAsync();
        UpdateTaskProgress(db);
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
    private async Task DeleteStageAsync(LocalTaskStage stage)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(stage.Id);
        if (entity is null) return;

        db.TaskStages.Remove(entity);
        await db.SaveChangesAsync();

        if (stage.IsSynced)
            await _sync.QueueOperationAsync("Stage", stage.Id, SyncOperation.Delete, new { });

        await LoadAsync();
        UpdateTaskProgress(db);
    }

    private void UpdateTaskProgress(LocalDbContext db)
    {
        if (Task is null) return;
        var taskId = Task.Id;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var taskEntity = await ctx.Tasks.FindAsync(taskId);
            if (taskEntity is null) return;
            var stages = await ctx.TaskStages.Where(s => s.TaskId == taskId).ToListAsync();
            taskEntity.TotalStages = stages.Count;
            taskEntity.CompletedStages = stages.Count(s => s.Status == StageStatus.Completed);
            await ctx.SaveChangesAsync();
        });
    }
}
