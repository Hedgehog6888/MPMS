using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class MaterialsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;

    [ObservableProperty] private ObservableCollection<LocalMaterial> _materials = [];
    [ObservableProperty] private string _searchText = string.Empty;

    public MaterialsViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _sync = sync;
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Materials.AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(m => m.Name.Contains(SearchText) ||
                (m.Description != null && m.Description.Contains(SearchText)));

        var list = await query.OrderBy(m => m.Name).ToListAsync();
        Materials = new ObservableCollection<LocalMaterial>(list);
    }

    public async Task SaveNewMaterialAsync(CreateMaterialRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var material = new LocalMaterial
        {
            Id = localId,
            Name = req.Name,
            Unit = req.Unit,
            Description = req.Description,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Materials.Add(material);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Material", localId, SyncOperation.Create,
            req with { Id = localId });

        await LoadAsync();
    }

    public async Task SaveUpdatedMaterialAsync(Guid id, UpdateMaterialRequest req)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var material = await db.Materials.FindAsync(id);
        if (material is null) return;

        material.Name = req.Name;
        material.Unit = req.Unit;
        material.Description = req.Description;
        material.IsSynced = false;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Material", id, SyncOperation.Update, req);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteMaterialAsync(LocalMaterial material)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Materials.FindAsync(material.Id);
        if (entity is null) return;

        db.Materials.Remove(entity);
        await db.SaveChangesAsync();

        if (material.IsSynced)
            await _sync.QueueOperationAsync("Material", material.Id, SyncOperation.Delete, new { });

        await LoadAsync();
    }
}
