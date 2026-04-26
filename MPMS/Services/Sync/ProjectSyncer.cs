using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using System.Text.Json;

namespace MPMS.Services.Sync;

public class ProjectSyncer : IEntitySyncer
{
    private readonly IApiService _api;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectSyncer(IApiService api, JsonSerializerOptions jsonOptions)
    {
        _api = api;
        _jsonOptions = jsonOptions;
    }

    public bool CanHandle(string entityType) => entityType == "Project";

    public Task PrepareAsync(LocalDbContext db) => Task.CompletedTask;

    public async Task PullAsync(LocalDbContext db)
    {
        var projects = await _api.GetProjectsAsync();
        if (projects is null) return;

        var existingProjects = await db.Projects.ToDictionaryAsync(p => p.Id);
        foreach (var p in projects)
        {
            if (existingProjects.TryGetValue(p.Id, out var local))
            {
                if (local.IsSynced)
                {
                    local.Name = p.Name;
                    local.Description = p.Description;
                    local.Client = p.Client;
                    local.Address = p.Address;
                    local.StartDate = p.StartDate;
                    local.EndDate = p.EndDate;
                    local.Status = Enum.Parse<ProjectStatus>(p.Status);
                    local.ManagerId = p.ManagerId;
                    local.ManagerName = p.ManagerName;
                    local.CreatedAt = p.CreatedAt;
                    local.UpdatedAt = p.UpdatedAt;
                    local.IsMarkedForDeletion = p.IsMarkedForDeletion;
                    local.IsArchived = p.IsArchived;
                    local.IsSynced = true;
                }
                else
                {
                    local.Name = p.Name;
                    local.Description = p.Description;
                    local.Client = p.Client;
                    local.Address = p.Address;
                    local.StartDate = p.StartDate;
                    local.EndDate = p.EndDate;
                    local.Status = Enum.Parse<ProjectStatus>(p.Status);
                    local.ManagerId = p.ManagerId;
                    local.ManagerName = p.ManagerName;
                    local.CreatedAt = p.CreatedAt;
                    local.UpdatedAt = p.UpdatedAt;
                    local.IsMarkedForDeletion = p.IsMarkedForDeletion;
                    local.IsArchived = p.IsArchived;
                }
            }
            else
            {
                db.Projects.Add(new LocalProject
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Client = p.Client,
                    Address = p.Address,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    Status = Enum.Parse<ProjectStatus>(p.Status),
                    ManagerId = p.ManagerId,
                    ManagerName = p.ManagerName,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    IsMarkedForDeletion = p.IsMarkedForDeletion,
                    IsArchived = p.IsArchived,
                    IsSynced = true
                });
            }
        }

        var serverProjectIds = projects.Select(p => p.Id).ToHashSet();
        var orphanProjectIds = await db.Projects
            .Where(p => p.IsSynced && !serverProjectIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
        
        foreach (var pid in orphanProjectIds)
            await LocalDbGraphDeletion.PermanentlyDeleteProjectGraphAsync(db, pid);
    }

    public async Task<bool> PushAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteProjectAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateProjectRequest>(op.Payload, _jsonOptions);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateProjectAsync(req);
            if (created is null) return false;
            
            var local = await db.Projects.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateProjectRequest>(op.Payload, _jsonOptions);
        if (updateReq is null) return false;
        var updated = await _api.UpdateProjectAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.Projects.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }
}
