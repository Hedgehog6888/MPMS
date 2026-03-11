using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class ProjectsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;

    [ObservableProperty] private ObservableCollection<LocalProject> _projects = [];
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
        Projects = new ObservableCollection<LocalProject>(list);
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
}
