using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class WarehouseViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;

    [ObservableProperty] private string _activeTab = "Materials";
    [ObservableProperty] private ObservableCollection<LocalMaterial> _materials = [];
    [ObservableProperty] private ObservableCollection<LocalEquipment> _equipments = [];
    [ObservableProperty] private ObservableCollection<LocalMaterialCategory> _materialCategories = [];
    [ObservableProperty] private ObservableCollection<LocalEquipmentCategory> _equipmentCategories = [];
    [ObservableProperty] private ObservableCollection<MaterialCategoryFilterOption> _materialCategoryFilterOptions = [];
    [ObservableProperty] private ObservableCollection<MaterialCategoryFilterOption> _equipmentCategoryFilterOptions = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Guid? _selectedCategoryId;
    [ObservableProperty] private string _lifecycleFilter = "Все";
    [ObservableProperty] private string _materialStockFilter = "Все";
    [ObservableProperty] private string _equipmentStatusFilter = "Все";

    private bool _suppressWarehouseFilterReload;

    public IReadOnlyList<string> LifecycleFilterOptions { get; } = ["Все", "Активные", "Списанные"];

    public IReadOnlyList<string> MaterialStockFilterOptions { get; } =
        ["Все", "Есть остаток", "Нет в наличии"];

    public IReadOnlyList<string> EquipmentStatusFilterOptions { get; } =
        ["Все", "Доступно", "Используется", "На обслуживании"];

    public bool CanManage =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    public bool CanViewHistory =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager" or "Foreman";

    public WarehouseViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    partial void OnSearchTextChanged(string value) => _ = LoadAsync();

    partial void OnSelectedCategoryIdChanged(Guid? value)
    {
        if (_suppressWarehouseFilterReload) return;
        _ = LoadAsync();
    }

    partial void OnLifecycleFilterChanged(string value) => _ = LoadAsync();

    partial void OnMaterialStockFilterChanged(string value)
    {
        if (_suppressWarehouseFilterReload) return;
        _ = LoadAsync();
    }

    partial void OnEquipmentStatusFilterChanged(string value)
    {
        if (_suppressWarehouseFilterReload) return;
        _ = LoadAsync();
    }

    partial void OnActiveTabChanged(string value)
    {
        _suppressWarehouseFilterReload = true;
        try
        {
            SelectedCategoryId = null;
            MaterialStockFilter = "Все";
            EquipmentStatusFilter = "Все";
        }
        finally
        {
            _suppressWarehouseFilterReload = false;
        }

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var cats = await db.MaterialCategories.OrderBy(c => c.Name).ToListAsync();
        MaterialCategories = new ObservableCollection<LocalMaterialCategory>(cats);

        var eqCats = await db.EquipmentCategories.OrderBy(c => c.Name).ToListAsync();
        EquipmentCategories = new ObservableCollection<LocalEquipmentCategory>(eqCats);

        var matFilterOpts = new List<MaterialCategoryFilterOption> { new(null, "Все категории") };
        matFilterOpts.AddRange(cats.Select(c => new MaterialCategoryFilterOption(c.Id, c.Name)));
        MaterialCategoryFilterOptions = new ObservableCollection<MaterialCategoryFilterOption>(matFilterOpts);

        var eqFilterOpts = new List<MaterialCategoryFilterOption> { new(null, "Все категории") };
        eqFilterOpts.AddRange(eqCats.Select(c => new MaterialCategoryFilterOption(c.Id, c.Name)));
        EquipmentCategoryFilterOptions = new ObservableCollection<MaterialCategoryFilterOption>(eqFilterOpts);

        if (ActiveTab == "Materials" && SelectedCategoryId is { } mid && cats.All(c => c.Id != mid))
        {
            _suppressWarehouseFilterReload = true;
            try
            {
                SelectedCategoryId = null;
            }
            finally
            {
                _suppressWarehouseFilterReload = false;
            }
        }
        else if (ActiveTab == "Equipment" && SelectedCategoryId is { } eid && eqCats.All(c => c.Id != eid))
        {
            _suppressWarehouseFilterReload = true;
            try
            {
                SelectedCategoryId = null;
            }
            finally
            {
                _suppressWarehouseFilterReload = false;
            }
        }

        var search = SearchHelper.Normalize(SearchText);

        var matQuery = db.Materials.AsQueryable();
        if (SelectedCategoryId.HasValue)
            matQuery = matQuery.Where(m => m.CategoryId == SelectedCategoryId);
        var matList = await matQuery.ToListAsync();
        if (search is not null)
            matList = matList.Where(m =>
                SearchHelper.ContainsIgnoreCase(m.Name, search) ||
                SearchHelper.ContainsIgnoreCase(m.Description, search) ||
                SearchHelper.ContainsIgnoreCase(m.CategoryName, search) ||
                SearchHelper.ContainsIgnoreCase(m.InventoryNumber, search)).ToList();

        matList = LifecycleFilter switch
        {
            "Активные" => matList.Where(m => !m.IsWrittenOff).ToList(),
            "Списанные" => matList.Where(m => m.IsWrittenOff).ToList(),
            _ => matList
        };

        matList = MaterialStockFilter switch
        {
            "Есть остаток" => matList.Where(m => m.Quantity > 0 && !m.IsWrittenOff).ToList(),
            "Нет в наличии" => matList.Where(m => m.Quantity <= 0 && !m.IsWrittenOff).ToList(),
            _ => matList
        };

        matList = matList
            .OrderBy(m => m.IsWrittenOff ? 2 : m.Quantity <= 0 ? 1 : 0)
            .ThenBy(m => m.Name)
            .ToList();
        Materials = new ObservableCollection<LocalMaterial>(matList);

        var eqQuery = db.Equipments.AsQueryable();
        if (SelectedCategoryId.HasValue)
            eqQuery = eqQuery.Where(e => e.CategoryId == SelectedCategoryId);
        var eqList = await eqQuery.ToListAsync();
        if (search is not null)
            eqList = eqList.Where(e =>
                SearchHelper.ContainsIgnoreCase(e.Name, search) ||
                SearchHelper.ContainsIgnoreCase(e.Description, search) ||
                SearchHelper.ContainsIgnoreCase(e.CategoryName, search) ||
                SearchHelper.ContainsIgnoreCase(e.InventoryNumber, search)).ToList();

        eqList = LifecycleFilter switch
        {
            "Активные" => eqList.Where(e => !e.IsWrittenOff).ToList(),
            "Списанные" => eqList.Where(e => e.IsWrittenOff).ToList(),
            _ => eqList
        };

        eqList = EquipmentStatusFilter switch
        {
            "Доступно" => eqList.Where(e => e.Status == "Available").ToList(),
            "Используется" => eqList.Where(e => e.Status == "InUse").ToList(),
            "На обслуживании" => eqList.Where(e => e.Status == "Maintenance").ToList(),
            _ => eqList
        };

        eqList = eqList
            .OrderBy(e => e.IsWrittenOff ? 1 : 0)
            .ThenBy(e => e.Name)
            .ToList();
        Equipments = new ObservableCollection<LocalEquipment>(eqList);
    }

    public async Task<List<LocalMaterialStockMovement>> GetMaterialHistoryAsync(Guid materialId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entries = await db.MaterialStockMovements
            .Where(m => m.MaterialId == materialId)
            .OrderByDescending(m => m.OccurredAt)
            .ToListAsync();

        // Populate UserName from Users table if missing
        var userIds = entries.Where(e => e.UserId.HasValue && e.UserName == null)
            .Select(e => e.UserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var users = await db.Users.Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name }).ToListAsync();
            var userMap = users.ToDictionary(u => u.Id, u => u.Name);
            foreach (var e in entries.Where(e => e.UserId.HasValue))
                if (userMap.TryGetValue(e.UserId!.Value, out var name))
                    e.UserName = name;
        }

        if (_auth.UserRole is "Foreman")
            return await FilterHistoryForForemanAsync(db, entries);
        if (_auth.UserRole is "Project Manager" or "ProjectManager" or "Manager")
            return await FilterHistoryForManagerAsync(db, entries);

        return entries;
    }

    public async Task<List<LocalEquipmentHistoryEntry>> GetEquipmentHistoryAsync(Guid equipmentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entries = await db.EquipmentHistoryEntries
            .Where(e => e.EquipmentId == equipmentId)
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync();

        var userIds = entries.Where(e => e.UserId.HasValue && e.UserName == null)
            .Select(e => e.UserId!.Value).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var users = await db.Users.Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name }).ToListAsync();
            var userMap = users.ToDictionary(u => u.Id, u => u.Name);
            foreach (var e in entries.Where(e => e.UserId.HasValue))
                if (userMap.TryGetValue(e.UserId!.Value, out var name))
                    e.UserName = name;
        }

        if (_auth.UserRole is "Foreman")
            return await FilterEquipmentHistoryForForemanAsync(db, entries);
        if (_auth.UserRole is "Project Manager" or "ProjectManager" or "Manager")
            return await FilterEquipmentHistoryForManagerAsync(db, entries);

        return entries;
    }

    private async Task<List<LocalMaterialStockMovement>> FilterHistoryForForemanAsync(
        LocalDbContext db, List<LocalMaterialStockMovement> entries)
    {
        if (_auth.UserId is not { } uid) return [];
        // Прораб видит историю своих работников и прорабов на общих проектах
        var myProjects = await db.ProjectMembers
            .Where(m => m.UserId == uid)
            .Select(m => m.ProjectId).Distinct().ToListAsync();
        var allowedUsers = await db.ProjectMembers
            .Where(m => myProjects.Contains(m.ProjectId) &&
                        (m.UserRole == "Foreman" || m.UserRole == "Worker"))
            .Select(m => m.UserId).Distinct().ToListAsync();
        allowedUsers.Add(uid);
        return entries.Where(e => e.UserId.HasValue && allowedUsers.Contains(e.UserId.Value)).ToList();
    }

    private async Task<List<LocalMaterialStockMovement>> FilterHistoryForManagerAsync(
        LocalDbContext db, List<LocalMaterialStockMovement> entries)
    {
        if (_auth.UserId is not { } uid) return entries;
        // Менеджер видит историю исполнителей своих проектов
        var myProjects = await db.Projects
            .Where(p => p.ManagerId == uid)
            .Select(p => p.Id).Distinct().ToListAsync();
        var allowedUsers = await db.ProjectMembers
            .Where(m => myProjects.Contains(m.ProjectId))
            .Select(m => m.UserId).Distinct().ToListAsync();
        allowedUsers.Add(uid);
        return entries.Where(e => !e.UserId.HasValue || allowedUsers.Contains(e.UserId.Value)).ToList();
    }

    private async Task<List<LocalEquipmentHistoryEntry>> FilterEquipmentHistoryForForemanAsync(
        LocalDbContext db, List<LocalEquipmentHistoryEntry> entries)
    {
        if (_auth.UserId is not { } uid) return [];
        var myProjects = await db.ProjectMembers
            .Where(m => m.UserId == uid)
            .Select(m => m.ProjectId).Distinct().ToListAsync();
        var allowedUsers = await db.ProjectMembers
            .Where(m => myProjects.Contains(m.ProjectId) &&
                        (m.UserRole == "Foreman" || m.UserRole == "Worker"))
            .Select(m => m.UserId).Distinct().ToListAsync();
        allowedUsers.Add(uid);
        return entries.Where(e => e.UserId.HasValue && allowedUsers.Contains(e.UserId.Value)).ToList();
    }

    private async Task<List<LocalEquipmentHistoryEntry>> FilterEquipmentHistoryForManagerAsync(
        LocalDbContext db, List<LocalEquipmentHistoryEntry> entries)
    {
        if (_auth.UserId is not { } uid) return entries;
        var myProjects = await db.Projects
            .Where(p => p.ManagerId == uid)
            .Select(p => p.Id).Distinct().ToListAsync();
        var allowedUsers = await db.ProjectMembers
            .Where(m => myProjects.Contains(m.ProjectId))
            .Select(m => m.UserId).Distinct().ToListAsync();
        allowedUsers.Add(uid);
        return entries.Where(e => !e.UserId.HasValue || allowedUsers.Contains(e.UserId.Value)).ToList();
    }

    // ── Material operations ───────────────────────────────────────────────────

    public async Task SaveNewMaterialAsync(string name, string? unit, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, decimal initialQty, decimal? cost = null,
        string? inventoryNumber = null)
    {
        var localId = Guid.NewGuid();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var material = new LocalMaterial
        {
            Id = localId,
            Name = name,
            Unit = unit,
            Description = description,
            Quantity = initialQty < 0 ? 0 : initialQty,
            Cost = cost,
            InventoryNumber = string.IsNullOrWhiteSpace(inventoryNumber) ? null : inventoryNumber.Trim(),
            CategoryId = categoryId,
            CategoryName = categoryName,
            ImagePath = imagePath,
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Materials.Add(material);

        if (initialQty > 0)
        {
            db.MaterialStockMovements.Add(new LocalMaterialStockMovement
            {
                Id = Guid.NewGuid(),
                MaterialId = localId,
                OccurredAt = DateTime.UtcNow,
                Delta = initialQty,
                QuantityAfter = initialQty,
                OperationType = "Addition",
                Comment = "Начальное количество",
                UserId = _auth.UserId,
                UserName = _auth.UserName
            });
        }

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Material", localId, SyncOperation.Create,
            new CreateMaterialRequest(
                Name: name,
                Unit: unit,
                Description: description,
                Id: localId,
                InitialQuantity: initialQty < 0 ? 0 : initialQty,
                CategoryId: categoryId,
                ImagePath: imagePath,
                Cost: cost,
                InventoryNumber: material.InventoryNumber));
        await LoadAsync();
    }

    public async Task SaveNewEquipmentAsync(string name, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, string? inventoryNumber)
    {
        var localId = Guid.NewGuid();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var equipment = new LocalEquipment
        {
            Id = localId,
            Name = name,
            Description = description,
            CategoryId = categoryId,
            CategoryName = categoryName,
            ImagePath = imagePath,
            InventoryNumber = inventoryNumber,
            Status = "Available",
            IsSynced = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Equipments.Add(equipment);

        db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
        {
            Id = Guid.NewGuid(),
            EquipmentId = localId,
            OccurredAt = DateTime.UtcNow,
            EventType = "Added",
            NewStatus = "Available",
            UserId = _auth.UserId,
            UserName = _auth.UserName,
            Comment = "Добавлено на склад"
        });

        await db.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task UpdateMaterialAsync(Guid id, string name, string? unit, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, decimal? cost = null,
        string? inventoryNumber = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var m = await db.Materials.FindAsync(id);
        if (m is null) return;
        m.Name = name;
        m.Unit = unit;
        m.Description = description;
        m.CategoryId = categoryId;
        m.CategoryName = categoryName;
        m.ImagePath = imagePath;
        m.Cost = cost;
        m.InventoryNumber = string.IsNullOrWhiteSpace(inventoryNumber) ? null : inventoryNumber.Trim();
        m.UpdatedAt = DateTime.UtcNow;
        m.IsSynced = false;
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Material", id, SyncOperation.Update,
            new UpdateMaterialRequest(
                Name: name,
                Unit: unit,
                Description: description,
                CategoryId: categoryId,
                ImagePath: imagePath,
                Cost: cost,
                InventoryNumber: m.InventoryNumber));
        await LoadAsync();
    }

    public async Task UpdateEquipmentAsync(Guid id, string name, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, string? inventoryNumber)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var e = await db.Equipments.FindAsync(id);
        if (e is null) return;
        e.Name = name;
        e.Description = description;
        e.CategoryId = categoryId;
        e.CategoryName = categoryName;
        e.ImagePath = imagePath;
        e.InventoryNumber = inventoryNumber;
        e.UpdatedAt = DateTime.UtcNow;
        e.IsSynced = false;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task AddMaterialQuantityAsync(Guid materialId, decimal amount, string? comment)
    {
        if (amount <= 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var m = await db.Materials.FindAsync(materialId);
        if (m is null) return;
        m.Quantity += amount;
        m.UpdatedAt = DateTime.UtcNow;
        m.IsSynced = false;
        db.MaterialStockMovements.Add(new LocalMaterialStockMovement
        {
            Id = Guid.NewGuid(),
            MaterialId = materialId,
            OccurredAt = DateTime.UtcNow,
            Delta = amount,
            QuantityAfter = m.Quantity,
            OperationType = "Addition",
            Comment = comment,
            UserId = _auth.UserId,
            UserName = _auth.UserName
        });
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task WriteOffMaterialAsync(Guid materialId, string? comment)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var m = await db.Materials.FindAsync(materialId);
        if (m is null) return;
        m.IsWrittenOff = true;
        m.WrittenOffAt = DateTime.UtcNow;
        m.WrittenOffComment = comment;
        m.UpdatedAt = DateTime.UtcNow;
        m.IsSynced = false;
        db.MaterialStockMovements.Add(new LocalMaterialStockMovement
        {
            Id = Guid.NewGuid(),
            MaterialId = materialId,
            OccurredAt = DateTime.UtcNow,
            Delta = -m.Quantity,
            QuantityAfter = 0,
            OperationType = "WriteOff",
            Comment = comment,
            UserId = _auth.UserId,
            UserName = _auth.UserName
        });
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task WriteOffEquipmentAsync(Guid equipmentId, string? comment)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var e = await db.Equipments.FindAsync(equipmentId);
        if (e is null) return;
        e.IsWrittenOff = true;
        e.WrittenOffAt = DateTime.UtcNow;
        e.WrittenOffComment = comment;
        e.UpdatedAt = DateTime.UtcNow;
        e.IsSynced = false;
        db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            OccurredAt = DateTime.UtcNow,
            EventType = "WrittenOff",
            PreviousStatus = e.Status,
            NewStatus = "WrittenOff",
            UserId = _auth.UserId,
            UserName = _auth.UserName,
            Comment = comment
        });
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task DeleteMaterialAsync(Guid materialId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var m = await db.Materials.FindAsync(materialId);
        if (m is null) return;
        db.Materials.Remove(m);
        await db.SaveChangesAsync();
        if (m.IsSynced)
            await _sync.QueueOperationAsync("Material", materialId, SyncOperation.Delete, new { });
        await LoadAsync();
    }

    public async Task DeleteEquipmentAsync(Guid equipmentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var e = await db.Equipments.FindAsync(equipmentId);
        if (e is null) return;
        db.Equipments.Remove(e);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    /// <summary>Partial quantity write-off (Consumption) — does not mark the material as written off.</summary>
    public async Task ConsumeMaterialAsync(Guid materialId, decimal amount, string? comment)
    {
        if (amount <= 0) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var m = await db.Materials.FindAsync(materialId);
        if (m is null) return;
        m.Quantity = Math.Max(0, m.Quantity - amount);
        m.UpdatedAt = DateTime.UtcNow;
        m.IsSynced = false;
        db.MaterialStockMovements.Add(new LocalMaterialStockMovement
        {
            Id = Guid.NewGuid(),
            MaterialId = materialId,
            OccurredAt = DateTime.UtcNow,
            Delta = -amount,
            QuantityAfter = m.Quantity,
            OperationType = "Consumption",
            Comment = comment,
            UserId = _auth.UserId,
            UserName = _auth.UserName
        });
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    public async Task SaveNewMaterialCategoryAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.MaterialCategories.Add(new LocalMaterialCategory { Id = Guid.NewGuid(), Name = name.Trim() });
        await db.SaveChangesAsync();
        var cats = await db.MaterialCategories.OrderBy(c => c.Name).ToListAsync();
        MaterialCategories = new ObservableCollection<LocalMaterialCategory>(cats);
    }

    public async Task SaveNewEquipmentCategoryAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = Guid.NewGuid(), Name = name.Trim() });
        await db.SaveChangesAsync();
        var cats = await db.EquipmentCategories.OrderBy(c => c.Name).ToListAsync();
        EquipmentCategories = new ObservableCollection<LocalEquipmentCategory>(cats);
    }
}
