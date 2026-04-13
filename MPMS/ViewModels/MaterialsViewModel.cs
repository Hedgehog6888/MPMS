using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class MaterialsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private bool _suppressCategoryFilterReload;
    private bool _suppressUnitFilterReload;

    [ObservableProperty] private ObservableCollection<LocalMaterial> _materials = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Guid? _categoryFilter;
    [ObservableProperty] private ObservableCollection<MaterialCategoryFilterOption> _categoryFilterOptions = [];
    [ObservableProperty] private string _unitFilter = "Все";
    [ObservableProperty] private ObservableCollection<string> _unitFilterOptions = [];
    [ObservableProperty] private string _stockFilter = "Все";

    public IReadOnlyList<string> StockFilterOptions { get; } = ["Все", "Активные", "Списанные"];

    public MaterialsViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _unitFilterOptions.Add("Все");
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    partial void OnCategoryFilterChanged(Guid? value)
    {
        if (_suppressCategoryFilterReload) return;
        _ = LoadAsync();
    }

    partial void OnUnitFilterChanged(string value)
    {
        if (_suppressUnitFilterReload) return;
        _ = LoadAsync();
    }

    partial void OnStockFilterChanged(string value) => _ = LoadAsync();

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var categories = await db.MaterialCategories.OrderBy(c => c.Name).ToListAsync();
        var categoryOpts = new List<MaterialCategoryFilterOption> { new(null, "Все категории") };
        categoryOpts.AddRange(categories.Select(c => new MaterialCategoryFilterOption(c.Id, c.Name)));

        _suppressCategoryFilterReload = true;
        try
        {
            CategoryFilterOptions = new ObservableCollection<MaterialCategoryFilterOption>(categoryOpts);
            if (CategoryFilter is { } cid && categories.All(c => c.Id != cid))
                CategoryFilter = null;
        }
        finally
        {
            _suppressCategoryFilterReload = false;
        }

        var distinctUnits = await db.Materials
            .Where(m => m.Unit != null && m.Unit != "")
            .Select(m => m.Unit!)
            .Distinct()
            .OrderBy(u => u)
            .ToListAsync();

        var prevUnit = UnitFilter;
        var needRebuildUnits = UnitFilterOptions.Count != distinctUnits.Count + 1
            || !UnitFilterOptions.Skip(1).SequenceEqual(distinctUnits);
        if (needRebuildUnits)
        {
            _suppressUnitFilterReload = true;
            try
            {
                UnitFilterOptions = new ObservableCollection<string> { "Все" };
                foreach (var u in distinctUnits)
                    UnitFilterOptions.Add(u);
                UnitFilter = UnitFilterOptions.Contains(prevUnit) ? prevUnit : "Все";
            }
            finally
            {
                _suppressUnitFilterReload = false;
            }
        }

        var list = await db.Materials.OrderBy(m => m.Name).ToListAsync();

        if (CategoryFilter is { } filterCat)
            list = list.Where(m => m.CategoryId == filterCat).ToList();

        if (UnitFilter is not null && UnitFilter != "Все")
            list = list.Where(m => string.Equals(m.Unit, UnitFilter, StringComparison.Ordinal)).ToList();

        list = StockFilter switch
        {
            "Активные" => list.Where(m => !m.IsWrittenOff).ToList(),
            "Списанные" => list.Where(m => m.IsWrittenOff).ToList(),
            _ => list
        };

        var searchTerm = SearchHelper.Normalize(SearchText);
        if (searchTerm is not null)
            list = list.Where(m => SearchHelper.ContainsIgnoreCase(m.Name, searchTerm) ||
                SearchHelper.ContainsIgnoreCase(m.Description, searchTerm)).ToList();

        Materials = new ObservableCollection<LocalMaterial>(list);
    }

    public async Task SaveNewMaterialAsync(CreateMaterialRequest req, Guid localId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var inv = await InventoryNumbers.NextMaterialAsync(db, req.InventoryNumber);
        var material = new LocalMaterial
        {
            Id = localId,
            Name = req.Name,
            Unit = req.Unit,
            Description = req.Description,
            Quantity = req.InitialQuantity < 0 ? 0 : req.InitialQuantity,
            Cost = req.Cost,
            InventoryNumber = inv,
            CategoryId = req.CategoryId,
            ImagePath = req.ImagePath,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Materials.Add(material);
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Material", localId, SyncOperation.Create,
            req with { Id = localId, InventoryNumber = material.InventoryNumber });
        await LogActivityAsync(db, $"Создан материал «{material.Name}»", "Material", localId, ActivityActionKind.Created);

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
        material.Cost = req.Cost;
        material.CategoryId = req.CategoryId;
        material.ImagePath = req.ImagePath;
        material.UpdatedAt = DateTime.UtcNow;
        material.IsSynced = false;

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Material", id, SyncOperation.Update,
            req with
            {
                InventoryNumber = material.InventoryNumber,
                CategoryId = material.CategoryId,
                ImagePath = material.ImagePath,
                Cost = material.Cost,
                IsWrittenOff = material.IsWrittenOff,
                WrittenOffAt = material.WrittenOffAt,
                WrittenOffComment = material.WrittenOffComment,
                IsArchived = material.IsArchived
            });
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

        await _sync.QueueOperationAsync("Material", material.Id, SyncOperation.Delete, new { });

        await LoadAsync();
    }

    private async Task LogActivityAsync(LocalDbContext db, string actionText, string entityType, Guid entityId, string? actionType = null)
    {
        var session = await db.AuthSessions.FindAsync(1);
        var userName = session?.UserName ?? "Система";
        var userId = session?.UserId;
        var actorRole = session?.UserRole;
        var parts = userName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : userName.Length > 0 ? $"{userName[0]}" : "?";

        var log = new LocalActivityLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActorRole = actorRole,
            UserName = userName,
            UserInitials = initials.ToUpper(),
            UserColor = "#1B6EC2",
            ActionType = actionType,
            ActionText = actionText,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow
        };

        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        await _sync.QueueLocalActivityLogAsync(log);
    }
}

public sealed class MaterialCategoryFilterOption
{
    public Guid? Id { get; }
    public string Name { get; }

    public MaterialCategoryFilterOption(Guid? id, string name)
    {
        Id = id;
        Name = name;
    }
}
