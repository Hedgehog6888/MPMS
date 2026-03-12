using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class StageItem : ObservableObject
{
    public LocalTaskStage Stage { get; init; } = null!;
    public string TaskName    { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public Guid   TaskId      { get; init; }
    public Guid   ProjectId   { get; init; }
}

public partial class StagesViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;

    [ObservableProperty] private ObservableCollection<StageItem> _stages = [];
    [ObservableProperty] private ObservableCollection<StageItem> _filteredStages = [];
    [ObservableProperty] private ObservableCollection<TaskStageGroup> _stageGroups = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "Все статусы";

    public List<string> StatusOptions { get; } =
        ["Все статусы", "Запланирован", "Выполняется", "Завершён"];

    public StagesViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _sync = sync;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        IsBusy = true;
        ClearMessages();
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var tasks = await db.Tasks.ToListAsync();
            var taskDict = tasks.ToDictionary(t => t.Id);

            var stageList = await db.TaskStages
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var items = stageList.Select(s =>
            {
                taskDict.TryGetValue(s.TaskId, out var task);
                return new StageItem
                {
                    Stage       = s,
                    TaskId      = s.TaskId,
                    TaskName    = task?.Name    ?? "—",
                    ProjectId   = task?.ProjectId ?? Guid.Empty,
                    ProjectName = task?.ProjectName ?? "—"
                };
            }).ToList();

            Stages = new ObservableCollection<StageItem>(items);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = Stages.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim().ToLower();
            query = query.Where(s =>
                s.Stage.Name.ToLower().Contains(term) ||
                s.TaskName.ToLower().Contains(term) ||
                s.ProjectName.ToLower().Contains(term));
        }

        if (StatusFilter != "Все статусы")
        {
            var targetStatus = StatusFilter switch
            {
                "Запланирован" => StageStatus.Planned,
                "Выполняется"  => StageStatus.InProgress,
                "Завершён"     => StageStatus.Completed,
                _              => (StageStatus?)null
            };
            if (targetStatus.HasValue)
                query = query.Where(s => s.Stage.Status == targetStatus.Value);
        }

        FilteredStages = new ObservableCollection<StageItem>(query);

        var groups = query
            .GroupBy(s => new { s.TaskId, s.TaskName, s.ProjectId, s.ProjectName })
            .OrderBy(g => g.Key.ProjectName)
            .ThenBy(g => g.Key.TaskName)
            .Select(g => new TaskStageGroup(g.Key.TaskId, g.Key.TaskName, g.Key.ProjectId, g.Key.ProjectName, g.ToList()))
            .ToList();
        StageGroups = new ObservableCollection<TaskStageGroup>(groups);
    }

    [RelayCommand]
    private async Task DeleteStageAsync(StageItem item)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TaskStages.FindAsync(item.Stage.Id);
        if (entity is null) return;

        db.TaskStages.Remove(entity);
        await db.SaveChangesAsync();

        if (item.Stage.IsSynced)
            await _sync.QueueOperationAsync("Stage", item.Stage.Id, SyncOperation.Delete, new { });

        await LogActivityAsync(db, $"Удалён этап «{item.Stage.Name}»", "Stage", item.Stage.Id);
        await LoadAsync();
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

    [RelayCommand]
    private async Task ChangeStageStatusAsync((StageItem item, StageStatus newStatus) args)
    {
        var (item, newStatus) = args;
        var vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        var task = await GetTaskForStageAsync(item.TaskId);
        if (task is null) return;
        vm.SetTask(task);
        var req = new UpdateStageRequest(item.Stage.Name, item.Stage.Description,
            item.Stage.AssignedUserId, newStatus);
        await vm.SaveUpdatedStageAsync(item.Stage.Id, req);
        await LoadAsync();
    }

    public async Task<LocalTask?> GetTaskForStageAsync(Guid taskId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks.FindAsync(taskId);
    }
}

public class TaskStageGroup
{
    public Guid TaskId { get; }
    public string TaskName { get; }
    public Guid ProjectId { get; }
    public string ProjectName { get; }
    public List<StageItem> Stages { get; }

    public TaskStageGroup(Guid taskId, string taskName, Guid projectId, string projectName, List<StageItem> stages)
    {
        TaskId = taskId;
        TaskName = taskName;
        ProjectId = projectId;
        ProjectName = projectName;
        Stages = stages;
    }
}
