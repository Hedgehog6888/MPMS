using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class ClosedProjectsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private CancellationTokenSource _loadCts = new();

    [ObservableProperty] private ObservableCollection<LocalProject> _projects = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private DateOnly? _startDateFilter = null;
    [ObservableProperty] private DateOnly? _endDateFilter = null;

    private List<LocalProject> _allProjects = [];

    public ClosedProjectsViewModel(IDbContextFactory<LocalDbContext> dbFactory,
        ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    public async Task LoadAsync()
    {
        _loadCts.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var query = db.Projects.Where(p => p.IsArchived);

            var userId = _auth.UserId;
            bool isManager = string.Equals(_auth.UserRole, "Project Manager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_auth.UserRole, "ProjectManager", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_auth.UserRole, "Manager", StringComparison.OrdinalIgnoreCase);
            bool isForeman = string.Equals(_auth.UserRole, "Foreman", StringComparison.OrdinalIgnoreCase);
            bool isWorker = string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

            if (userId.HasValue)
            {
                if (isManager)
                {
                    query = query.Where(p => p.ManagerId == userId.Value);
                }
                else if (isForeman)
                {
                    var assignedProjectIds = await db.ProjectMembers
                        .Where(m => m.UserId == userId.Value)
                        .Select(m => m.ProjectId)
                        .ToListAsync(ct);
                    query = query.Where(p => assignedProjectIds.Contains(p.Id));
                }
                else if (isWorker)
                {
                    var projectIdsFromTaskAssignee = await db.Tasks
                        .Where(t => t.AssignedUserId == userId.Value)
                        .Select(t => t.ProjectId)
                        .ToListAsync(ct);
                    var projectIdsFromTaskAssignees = await db.TaskAssignees
                        .Where(ta => ta.UserId == userId.Value)
                        .Join(db.Tasks, ta => ta.TaskId, t => t.Id, (_, t) => t.ProjectId)
                        .ToListAsync(ct);
                    var projectIdsFromStageAssignees = await db.StageAssignees
                        .Where(sa => sa.UserId == userId.Value)
                        .Join(db.TaskStages, sa => sa.StageId, s => s.Id, (_, s) => s.TaskId)
                        .Join(db.Tasks, tid => tid, t => t.Id, (_, t) => t.ProjectId)
                        .ToListAsync(ct);
                    var projectIdsFromStageAssigned = await db.TaskStages
                        .Where(s => s.AssignedUserId == userId.Value)
                        .Join(db.Tasks, s => s.TaskId, t => t.Id, (_, t) => t.ProjectId)
                        .ToListAsync(ct);
                    var workerProjectIds = projectIdsFromTaskAssignee
                        .Concat(projectIdsFromTaskAssignees)
                        .Concat(projectIdsFromStageAssignees)
                        .Concat(projectIdsFromStageAssigned)
                        .Distinct()
                        .ToList();
                    query = query.Where(p => workerProjectIds.Contains(p.Id));
                }
            }

            var list = await query.OrderByDescending(p => p.UpdatedAt).ToListAsync(ct);
            _allProjects = list;
            ApplySearch();
        }
        catch (OperationCanceledException) { }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch();
    }

    partial void OnStartDateFilterChanged(DateOnly? value)
    {
        ApplySearch();
    }

    partial void OnEndDateFilterChanged(DateOnly? value)
    {
        ApplySearch();
    }

    private void ApplySearch()
    {
        var filtered = _allProjects.AsEnumerable();

        // Search by name or client
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchTerm = SearchText.ToLower();
            filtered = filtered.Where(p => p.Name.ToLower().Contains(searchTerm) ||
                                         (p.Client != null && p.Client.ToLower().Contains(searchTerm)));
        }

        // Filter by start date
        if (StartDateFilter.HasValue)
        {
            filtered = filtered.Where(p => p.StartDate.HasValue && p.StartDate.Value >= StartDateFilter.Value);
        }

        // Filter by end date
        if (EndDateFilter.HasValue)
        {
            filtered = filtered.Where(p => p.EndDate.HasValue && p.EndDate.Value <= EndDateFilter.Value);
        }

        Projects = new ObservableCollection<LocalProject>(filtered.ToList());
    }
}
