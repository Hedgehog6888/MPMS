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
        ["Все", "Доступно", "Недоступно", "Используется", "Списано"];

    public bool CanManage =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager";

    public bool CanViewHistory =>
        _auth.UserRole is "Administrator" or "Admin" or "Project Manager" or "ProjectManager" or "Manager" or "Foreman"
        || string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase);

    public WarehouseViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
    }

    private static bool ShouldBeUnavailable(EquipmentCondition condition) =>
        condition is EquipmentCondition.NeedsMaintenance or EquipmentCondition.Faulty;

    private static string ResolveStatusAfterConditionChange(LocalEquipment e, EquipmentCondition newCondition)
    {
        if (e.IsWrittenOff) return "Retired";
        if (ShouldBeUnavailable(newCondition)) return "Unavailable";
        if (e.Status is "Unavailable" or "3") return "Available";
        return e.Status;
    }

    private static EquipmentStatus? ToEquipmentStatusEnum(string status) => status switch
    {
        "Available" => EquipmentStatus.Available,
        "InUse" => EquipmentStatus.InUse,
        "CheckedOut" => EquipmentStatus.InUse,
        "Retired" => EquipmentStatus.Retired,
        "Unavailable" => EquipmentStatus.Unavailable,
        "3" => EquipmentStatus.Unavailable,
        _ => null
    };

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

        var cats = (await db.MaterialCategories.OrderBy(c => c.Name).ToListAsync())
            .GroupBy(c => (c.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Name).First())
            .OrderBy(c => c.Name)
            .ToList();
        MaterialCategories = new ObservableCollection<LocalMaterialCategory>(cats);

        var eqCats = (await db.EquipmentCategories.OrderBy(c => c.Name).ToListAsync())
            .GroupBy(c => (c.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Name).First())
            .OrderBy(c => c.Name)
            .ToList();
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
            "Недоступно" => eqList.Where(e => e.Status is "Unavailable" or "3").ToList(),
            "Используется" => eqList.Where(e => e.Status is "InUse" or "CheckedOut").ToList(),
            "Списано" => eqList.Where(e => e.Status == "Retired" || e.IsWrittenOff).ToList(),
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
        if (string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase))
        {
            if (_auth.UserId is not { } wUid) return [];
            return entries.Where(e => e.UserId == wUid).ToList();
        }

        // Админ и менеджер проекта — полная история по материалу
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
        if (string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase))
        {
            if (_auth.UserId is not { } wUid) return [];
            return entries.Where(e => e.UserId == wUid).ToList();
        }

        // Админ и менеджер проекта — полная история по оборудованию
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

    // ── Material operations ───────────────────────────────────────────────────

    /// <summary>Следующий инв. номер для отображения в форме (без резервирования).</summary>
    public async Task<string> PeekNextMaterialInventoryNumberAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await InventoryNumbers.NextMaterialAsync(db);
    }

    /// <summary>Следующий инв. номер для оборудования (без резервирования).</summary>
    public async Task<string> PeekNextEquipmentInventoryNumberAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await InventoryNumbers.NextEquipmentAsync(db);
    }

    public async Task SaveNewMaterialAsync(string name, string? unit, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, decimal initialQty, decimal? cost = null,
        string? preferredInventoryNumber = null)
    {
        var localId = Guid.NewGuid();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var inv = await InventoryNumbers.NextMaterialAsync(db, preferredInventoryNumber);
        var material = new LocalMaterial
        {
            Id = localId,
            Name = name,
            Unit = unit,
            Description = description,
            Quantity = initialQty < 0 ? 0 : initialQty,
            Cost = cost,
            InventoryNumber = inv,
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
        await LogActivityAsync(db, $"Создан материал «{name}»", "Material", localId, ActivityActionKind.Created);
        await LoadAsync();
    }

    public async Task SaveNewEquipmentAsync(string name, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, EquipmentCondition condition,
        string? preferredInventoryNumber = null)
    {
        var localId = Guid.NewGuid();
        await using var db = await _dbFactory.CreateDbContextAsync();
        var inv = await InventoryNumbers.NextEquipmentAsync(db, preferredInventoryNumber);
        var equipment = new LocalEquipment
        {
            Id = localId,
            Name = name,
            Description = description,
            CategoryId = categoryId,
            CategoryName = categoryName,
            ImagePath = imagePath,
            InventoryNumber = inv,
            Status = ShouldBeUnavailable(condition) ? "Unavailable" : "Available",
            Condition = condition.ToString(),
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
            NewStatus = ShouldBeUnavailable(condition) ? "Unavailable" : "Available",
            UserId = _auth.UserId,
            UserName = _auth.UserName,
            Comment = "Добавлено на склад"
        });

        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Equipment", localId, SyncOperation.Create,
            new CreateEquipmentRequest(
                Name: name,
                Description: description,
                CategoryId: categoryId,
                ImagePath: imagePath,
                InventoryNumber: equipment.InventoryNumber,
                Condition: condition,
                Id: localId));
        await _sync.QueueOperationAsync("EquipmentHistory", localId, SyncOperation.Create,
            new RecordEquipmentEventRequest(
                EventType: EquipmentHistoryEventType.Note,
                NewStatus: null,
                ProjectId: null,
                TaskId: null,
                Comment: "Добавлено на склад"));
        await LogActivityAsync(db, $"Создано оборудование «{name}»", "Equipment", localId, ActivityActionKind.Created);
        await LoadAsync();
    }

    public async Task UpdateMaterialAsync(Guid id, string name, string? unit, string? description,
        Guid? categoryId, string? categoryName, string? imagePath, decimal? cost = null)
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
        Guid? categoryId, string? categoryName, string? imagePath, EquipmentCondition condition)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var e = await db.Equipments.FindAsync(id);
        if (e is null) return;
        var previousStatus = e.Status;
        e.Name = name;
        e.Description = description;
        e.CategoryId = categoryId;
        e.CategoryName = categoryName;
        e.ImagePath = imagePath;
        e.Condition = condition.ToString();
        e.Status = ResolveStatusAfterConditionChange(e, condition);
        e.UpdatedAt = DateTime.UtcNow;
        e.IsSynced = false;
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("Equipment", id, SyncOperation.Update,
            new UpdateEquipmentRequest(
                Name: name,
                Description: description,
                CategoryId: categoryId,
                ImagePath: imagePath,
                InventoryNumber: e.InventoryNumber,
                Condition: condition,
                Status: ToEquipmentStatusEnum(e.Status)));
        if (!string.Equals(previousStatus, e.Status, StringComparison.Ordinal))
        {
            var newStatus = ToEquipmentStatusEnum(e.Status);
            if (newStatus.HasValue)
            {
                await _sync.QueueOperationAsync("EquipmentHistory", id, SyncOperation.Create,
                    new RecordEquipmentEventRequest(
                        EventType: EquipmentHistoryEventType.StatusChanged,
                        NewStatus: newStatus.Value,
                        ProjectId: null,
                        TaskId: null,
                        Comment: $"Статус автоматически изменен: {e.StatusDisplay} (по состоянию оборудования)"));
            }
        }
        await LoadAsync();
    }

    public async Task UpdateEquipmentConditionAsync(Guid id, EquipmentCondition condition, string? comment = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var e = await db.Equipments.FindAsync(id);
        if (e is null) return;
        if (string.Equals(e.Condition, condition.ToString(), StringComparison.Ordinal))
            return;

        var previousStatus = e.Status;
        e.Condition = condition.ToString();
        e.Status = ResolveStatusAfterConditionChange(e, condition);
        e.UpdatedAt = DateTime.UtcNow;
        e.IsSynced = false;
        await db.SaveChangesAsync();

        await _sync.QueueOperationAsync("Equipment", id, SyncOperation.Update,
            new UpdateEquipmentRequest(
                Name: e.Name,
                Description: e.Description,
                CategoryId: e.CategoryId,
                ImagePath: e.ImagePath,
                InventoryNumber: e.InventoryNumber,
                Condition: condition,
                Status: ToEquipmentStatusEnum(e.Status)));

        var conditionComment = string.IsNullOrWhiteSpace(comment)
            ? $"Состояние изменено: {e.ConditionDisplay}"
            : comment;
        await _sync.QueueOperationAsync("EquipmentHistory", id, SyncOperation.Create,
            new RecordEquipmentEventRequest(
                EventType: EquipmentHistoryEventType.Note,
                NewStatus: null,
                ProjectId: null,
                TaskId: null,
                Comment: conditionComment));
        if (!string.Equals(previousStatus, e.Status, StringComparison.Ordinal))
        {
            var newStatus = ToEquipmentStatusEnum(e.Status);
            if (newStatus.HasValue)
            {
                await _sync.QueueOperationAsync("EquipmentHistory", id, SyncOperation.Create,
                    new RecordEquipmentEventRequest(
                        EventType: EquipmentHistoryEventType.StatusChanged,
                        NewStatus: newStatus.Value,
                        ProjectId: null,
                        TaskId: null,
                        Comment: $"Статус автоматически изменен: {e.StatusDisplay} (по состоянию оборудования)"));
            }
        }

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
        await _sync.QueueOperationAsync("MaterialStockMovement", materialId, SyncOperation.Create,
            new RecordMaterialStockRequest(
                Delta: amount,
                OperationType: MaterialStockOperationType.Purchase,
                Comment: comment,
                ProjectId: null,
                TaskId: null));
        await LogActivityAsync(
            db,
            $"Пополнен материал «{m.Name}» на {FormatQuantity(amount, m.Unit)}",
            "Material",
            materialId,
            ActivityActionKind.Updated);
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
        var writeOffDelta = -m.Quantity;
        db.MaterialStockMovements.Add(new LocalMaterialStockMovement
        {
            Id = Guid.NewGuid(),
            MaterialId = materialId,
            OccurredAt = DateTime.UtcNow,
            Delta = writeOffDelta,
            QuantityAfter = 0,
            OperationType = "WriteOff",
            Comment = comment,
            UserId = _auth.UserId,
            UserName = _auth.UserName
        });
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("MaterialStockMovement", materialId, SyncOperation.Create,
            new RecordMaterialStockRequest(
                Delta: writeOffDelta,
                OperationType: MaterialStockOperationType.WriteOff,
                Comment: comment,
                ProjectId: null,
                TaskId: null));
        var m2 = await db.Materials.FindAsync(materialId);
        if (m2 is not null)
            await _sync.QueueOperationAsync("Material", materialId, SyncOperation.Update, SyncPayloads.Material(m2));
        await LogActivityAsync(
            db,
            $"Списан материал «{m.Name}» ({FormatQuantity(Math.Abs(writeOffDelta), m.Unit)})",
            "Material",
            materialId,
            ActivityActionKind.Deleted);
        await LoadAsync();
    }

    public async Task WriteOffEquipmentAsync(Guid equipmentId, string? comment)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var e = await db.Equipments.FindAsync(equipmentId);
        if (e is null) return;
        var previousStatus = e.Status;
        e.IsWrittenOff = true;
        e.WrittenOffAt = DateTime.UtcNow;
        e.WrittenOffComment = comment;
        e.Status = "Retired";
        e.UpdatedAt = DateTime.UtcNow;
        e.IsSynced = false;
        db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            OccurredAt = DateTime.UtcNow,
            EventType = "WrittenOff",
            PreviousStatus = previousStatus,
            NewStatus = "Retired",
            UserId = _auth.UserId,
            UserName = _auth.UserName,
            Comment = comment
        });
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("EquipmentHistory", equipmentId, SyncOperation.Create,
            new RecordEquipmentEventRequest(
                EventType: EquipmentHistoryEventType.WrittenOff,
                NewStatus: EquipmentStatus.Retired,
                ProjectId: null,
                TaskId: null,
                Comment: comment));
        var e2 = await db.Equipments.FindAsync(equipmentId);
        if (e2 is not null)
            await _sync.QueueOperationAsync("Equipment", equipmentId, SyncOperation.Update, SyncPayloads.Equipment(e2));
        await LogActivityAsync(
            db,
            $"Списано оборудование «{e.Name}»",
            "Equipment",
            equipmentId,
            ActivityActionKind.Deleted);
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
        var wasSynced = e.IsSynced;
        db.Equipments.Remove(e);
        await db.SaveChangesAsync();
        if (wasSynced)
            await _sync.QueueOperationAsync("Equipment", equipmentId, SyncOperation.Delete, new { });
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
        await _sync.QueueOperationAsync("MaterialStockMovement", materialId, SyncOperation.Create,
            new RecordMaterialStockRequest(
                Delta: -amount,
                OperationType: MaterialStockOperationType.Consumption,
                Comment: comment,
                ProjectId: null,
                TaskId: null));
        await LogActivityAsync(
            db,
            $"Списана часть материала «{m.Name}» ({FormatQuantity(amount, m.Unit)})",
            "Material",
            materialId,
            ActivityActionKind.Updated);
        await LoadAsync();
    }

    public async Task SaveNewMaterialCategoryAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var normalized = name.Trim();
        var existing = await db.MaterialCategories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == normalized.ToLower());
        if (existing is not null)
        {
            await LoadAsync();
            return;
        }

        var categoryId = Guid.NewGuid();
        db.MaterialCategories.Add(new LocalMaterialCategory { Id = categoryId, Name = normalized });
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("MaterialCategory", categoryId, SyncOperation.Create,
            new CreateMaterialCategoryRequest(normalized, categoryId));
        var cats = await db.MaterialCategories.OrderBy(c => c.Name).ToListAsync();
        MaterialCategories = new ObservableCollection<LocalMaterialCategory>(cats);
    }

    public async Task SaveNewEquipmentCategoryAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var normalized = name.Trim();
        var existing = await db.EquipmentCategories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == normalized.ToLower());
        if (existing is not null)
        {
            await LoadAsync();
            return;
        }

        var categoryId = Guid.NewGuid();
        db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = categoryId, Name = normalized });
        await db.SaveChangesAsync();
        await _sync.QueueOperationAsync("EquipmentCategory", categoryId, SyncOperation.Create,
            new CreateEquipmentCategoryRequest(normalized, categoryId));
        var cats = await db.EquipmentCategories.OrderBy(c => c.Name).ToListAsync();
        EquipmentCategories = new ObservableCollection<LocalEquipmentCategory>(cats);
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

    private static string FormatQuantity(decimal amount, string? unit)
    {
        var number = MaterialUnits.IsIntegerUnit(unit)
            ? decimal.Truncate(amount).ToString("0")
            : amount.ToString("0.##");
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }
}
