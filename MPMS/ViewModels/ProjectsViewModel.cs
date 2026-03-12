using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.ViewModels;

public partial class ProjectsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;

    [ObservableProperty] private ObservableCollection<LocalProject> _projects = [];
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _recentActivities = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusFilter = "Все";

    public IReadOnlyList<string> StatusOptions { get; } =
        ["Все", "Планирование", "В работе", "Завершён", "Отменён"];

    public ProjectsViewModel(IDbContextFactory<LocalDbContext> dbFactory,
        ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();
    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Projects.AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(p => p.Name.Contains(SearchText) ||
                (p.Client != null && p.Client.Contains(SearchText)));

        if (StatusFilter != "Все")
        {
            var status = StatusFilter switch
            {
                "Планирование" => ProjectStatus.Planning,
                "В работе"     => ProjectStatus.InProgress,
                "Завершён"     => ProjectStatus.Completed,
                "Отменён"      => ProjectStatus.Cancelled,
                _              => (ProjectStatus?)null
            };
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
        }

        var list = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        // Populate progress stats for each project
        var allTasks = await db.Tasks.ToListAsync();
        foreach (var project in list)
        {
            var projTasks = allTasks.Where(t => t.ProjectId == project.Id).ToList();
            project.TotalTasks = projTasks.Count;
            project.CompletedTasks = projTasks.Count(t => t.Status == TaskStatus.Completed);
        }

        Projects = new ObservableCollection<LocalProject>(list);

        // Load recent activity log (last 10 entries)
        var activities = await db.ActivityLogs
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .ToListAsync();
        RecentActivities = new ObservableCollection<LocalActivityLog>(activities);
    }

    public async Task SaveNewProjectAsync(CreateProjectRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var managerName = await db.Users
            .Where(u => u.Id == req.ManagerId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync() ?? "—";

        var project = new LocalProject
        {
            Id = localId,
            Name = req.Name,
            Description = req.Description,
            Client = req.Client,
            Address = req.Address,
            StartDate = req.StartDate,
            EndDate = req.EndDate,
            Status = ProjectStatus.Planning,
            ManagerId = req.ManagerId,
            ManagerName = managerName,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Projects.Add(project);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Project", localId, SyncOperation.Create,
            req with { Id = localId });

        await LogActivityAsync(db, $"Создан проект «{req.Name}»", "Project", localId);
        await LoadAsync();
    }

    public async Task SaveUpdatedProjectAsync(Guid id, UpdateProjectRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(id);
        if (project is null) return;

        var managerName = await db.Users
            .Where(u => u.Id == req.ManagerId)
            .Select(u => u.Name)
            .FirstOrDefaultAsync() ?? project.ManagerName;

        project.Name = req.Name;
        project.Description = req.Description;
        project.Client = req.Client;
        project.Address = req.Address;
        project.StartDate = req.StartDate;
        project.EndDate = req.EndDate;
        project.Status = req.Status;
        project.ManagerId = req.ManagerId;
        project.ManagerName = managerName;
        project.IsSynced = false;
        project.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Project", id, SyncOperation.Update, req);
        await LogActivityAsync(db, $"Обновлён проект «{req.Name}»", "Project", id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteProjectAsync(LocalProject project)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Projects.FindAsync(project.Id);
        if (entity is null) return;

        db.Projects.Remove(entity);
        await db.SaveChangesAsync();

        if (project.IsSynced)
            await _sync.QueueOperationAsync("Project", project.Id, SyncOperation.Delete, new { });

        await LoadAsync();
    }

    private async Task LogActivityAsync(LocalDbContext db, string actionText,
        string entityType, Guid entityId)
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
}
