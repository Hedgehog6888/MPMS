using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using System.Text.Json;

namespace MPMS.Services.Sync;

public class WarehouseSyncer : IEntitySyncer
{
    private readonly IApiService _api;
    private readonly JsonSerializerOptions _jsonOptions;

    public WarehouseSyncer(IApiService api, JsonSerializerOptions jsonOptions)
    {
        _api = api;
        _jsonOptions = jsonOptions;
    }

    public bool CanHandle(string entityType) =>
        entityType is "Material" or "MaterialCategory" or "EquipmentCategory" or
                     "MaterialStockMovement" or "Equipment" or "EquipmentHistory";

    public async Task PrepareAsync(LocalDbContext db)
    {
        await RecoverWarehouseFailedOperationsAsync(db);
        await EnsureWarehouseCategoriesReadyAsync(db);
    }

    private static async Task RecoverWarehouseFailedOperationsAsync(LocalDbContext db)
    {
        var recoverableTypes = new[]
        {
            "Material", "Equipment",
            "MaterialCategory", "EquipmentCategory",
            "MaterialStockMovement", "EquipmentHistory"
        };

        var failed = await db.PendingOperations
            .Where(p => p.IsFailed && recoverableTypes.Contains(p.EntityType))
            .ToListAsync();

        foreach (var op in failed)
        {
            op.IsFailed = false;
            op.RetryCount = 0;
            op.ErrorMessage = null;
        }

        if (failed.Count > 0)
            await db.SaveChangesAsync();
    }

    private async Task EnsureWarehouseCategoriesReadyAsync(LocalDbContext db)
    {
        var apiMatCats = await _api.GetMaterialCategoriesAsync() ?? [];
        var apiEqCats = await _api.GetEquipmentCategoriesAsync() ?? [];

        var localMatCats = await db.MaterialCategories.ToListAsync();
        foreach (var local in localMatCats)
        {
            if (apiMatCats.Any(c => c.Id == local.Id))
                continue;

            var byName = apiMatCats.FirstOrDefault(c =>
                string.Equals(c.Name, local.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                await RemapMaterialCategoryAsync(db, local.Id, byName.Id, byName.Name);
                continue;
            }

            var queued = await db.PendingOperations.AnyAsync(p =>
                !p.IsFailed &&
                p.EntityType == "MaterialCategory" &&
                p.OperationType == SyncOperation.Create &&
                p.EntityId == local.Id);
            if (!queued)
            {
                db.PendingOperations.Add(new PendingOperation
                {
                    EntityType = "MaterialCategory",
                    EntityId = local.Id,
                    OperationType = SyncOperation.Create,
                    Payload = JsonSerializer.Serialize(new CreateMaterialCategoryRequest(local.Name, local.Id), _jsonOptions),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        var localEqCats = await db.EquipmentCategories.ToListAsync();
        foreach (var local in localEqCats)
        {
            if (apiEqCats.Any(c => c.Id == local.Id))
                continue;

            var byName = apiEqCats.FirstOrDefault(c =>
                string.Equals(c.Name, local.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                await RemapEquipmentCategoryAsync(db, local.Id, byName.Id, byName.Name);
                continue;
            }

            var queued = await db.PendingOperations.AnyAsync(p =>
                !p.IsFailed &&
                p.EntityType == "EquipmentCategory" &&
                p.OperationType == SyncOperation.Create &&
                p.EntityId == local.Id);
            if (!queued)
            {
                db.PendingOperations.Add(new PendingOperation
                {
                    EntityType = "EquipmentCategory",
                    EntityId = local.Id,
                    OperationType = SyncOperation.Create,
                    Payload = JsonSerializer.Serialize(new CreateEquipmentCategoryRequest(local.Name, local.Id), _jsonOptions),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task RemapMaterialCategoryAsync(LocalDbContext db, Guid oldId, Guid newId, string newName)
    {
        if (oldId == newId) return;

        var targetCat = await db.MaterialCategories.FindAsync(newId);
        if (targetCat is null)
            db.MaterialCategories.Add(new LocalMaterialCategory { Id = newId, Name = newName });
        else
            targetCat.Name = newName;

        var mats = await db.Materials.Where(m => m.CategoryId == oldId).ToListAsync();
        foreach (var m in mats)
        {
            m.CategoryId = newId;
            m.CategoryName = newName;
            m.IsSynced = false;
        }

        var matOps = await db.PendingOperations
            .Where(p => !p.IsFailed && p.EntityType == "Material")
            .ToListAsync();
        foreach (var op in matOps)
        {
            if (op.OperationType == SyncOperation.Create)
            {
                var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload, _jsonOptions);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, _jsonOptions);
            }
            else if (op.OperationType == SyncOperation.Update)
            {
                var req = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload, _jsonOptions);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, _jsonOptions);
            }
        }

        var oldCat = await db.MaterialCategories.FindAsync(oldId);
        if (oldCat is not null)
            db.MaterialCategories.Remove(oldCat);
    }

    private async Task RemapEquipmentCategoryAsync(LocalDbContext db, Guid oldId, Guid newId, string newName)
    {
        if (oldId == newId) return;

        var targetCat = await db.EquipmentCategories.FindAsync(newId);
        if (targetCat is null)
            db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = newId, Name = newName });
        else
            targetCat.Name = newName;

        var equips = await db.Equipments.Where(e => e.CategoryId == oldId).ToListAsync();
        foreach (var e in equips)
        {
            e.CategoryId = newId;
            e.CategoryName = newName;
            e.IsSynced = false;
        }

        var eqOps = await db.PendingOperations
            .Where(p => !p.IsFailed && p.EntityType == "Equipment")
            .ToListAsync();
        foreach (var op in eqOps)
        {
            if (op.OperationType == SyncOperation.Create)
            {
                var req = JsonSerializer.Deserialize<CreateEquipmentRequest>(op.Payload, _jsonOptions);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, _jsonOptions);
            }
            else if (op.OperationType == SyncOperation.Update)
            {
                var req = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload, _jsonOptions);
                if (req?.CategoryId == oldId)
                    op.Payload = JsonSerializer.Serialize(req with { CategoryId = newId }, _jsonOptions);
            }
        }

        var oldCat = await db.EquipmentCategories.FindAsync(oldId);
        if (oldCat is not null)
            db.EquipmentCategories.Remove(oldCat);
    }

    public async Task PullAsync(LocalDbContext db)
    {
        // 1. Categories
        var matCats = await _api.GetMaterialCategoriesAsync();
        if (matCats is not null)
        {
            foreach (var c in matCats)
            {
                var ex = await db.MaterialCategories.FindAsync(c.Id);
                if (ex is null)
                    db.MaterialCategories.Add(new LocalMaterialCategory { Id = c.Id, Name = c.Name });
                else
                    ex.Name = c.Name;
            }
        }

        var eqCats = await _api.GetEquipmentCategoriesAsync();
        if (eqCats is not null)
        {
            foreach (var c in eqCats)
            {
                var ex = await db.EquipmentCategories.FindAsync(c.Id);
                if (ex is null)
                    db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = c.Id, Name = c.Name });
                else
                    ex.Name = c.Name;
            }
        }

        // 2. Materials
        var materials = await _api.GetMaterialsAsync();
        if (materials is not null)
        {
            var existingMats = await db.Materials.ToDictionaryAsync(m => m.Id);
            foreach (var m in materials)
            {
                if (existingMats.TryGetValue(m.Id, out var local))
                {
                    if (local.IsSynced)
                    {
                        local.Name = m.Name;
                        local.Unit = m.Unit;
                        local.Description = m.Description;
                        local.Quantity = m.Quantity;
                        local.Cost = m.Cost;
                        local.InventoryNumber = m.InventoryNumber;
                        local.CategoryId = m.CategoryId;
                        local.CategoryName = m.CategoryName;
                        local.ImagePath = m.ImagePath;
                        local.UpdatedAt = m.UpdatedAt;
                        local.IsWrittenOff = m.IsWrittenOff;
                        local.WrittenOffAt = m.WrittenOffAt;
                        local.WrittenOffComment = m.WrittenOffComment;
                        local.IsArchived = m.IsArchived;
                        local.IsSynced = true;
                    }
                }
                else
                {
                    db.Materials.Add(new LocalMaterial
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Unit = m.Unit,
                        Description = m.Description,
                        Quantity = m.Quantity,
                        Cost = m.Cost,
                        InventoryNumber = m.InventoryNumber,
                        CategoryId = m.CategoryId,
                        CategoryName = m.CategoryName,
                        ImagePath = m.ImagePath,
                        CreatedAt = m.CreatedAt,
                        UpdatedAt = m.UpdatedAt,
                        IsWrittenOff = m.IsWrittenOff,
                        WrittenOffAt = m.WrittenOffAt,
                        WrittenOffComment = m.WrittenOffComment,
                        IsArchived = m.IsArchived,
                        IsSynced = true
                    });
                }
            }

            var serverMaterialIds = materials.Select(m => m.Id).ToHashSet();
            var orphanMaterialIds = await db.Materials
                .Where(m => m.IsSynced && !serverMaterialIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync();
            foreach (var mid in orphanMaterialIds)
                await LocalDbGraphDeletion.PermanentlyDeleteMaterialGraphAsync(db, mid);
        }

        // 3. Stock movements
        var stockMoves = await _api.GetAllMaterialStockMovementsAsync();
        if (stockMoves is not null)
        {
            await db.MaterialStockMovements.ExecuteDeleteAsync();
            foreach (var x in stockMoves)
            {
                db.MaterialStockMovements.Add(new LocalMaterialStockMovement
                {
                    Id = x.Id,
                    MaterialId = x.MaterialId,
                    OccurredAt = x.OccurredAt,
                    Delta = x.Delta,
                    QuantityAfter = x.QuantityAfter,
                    OperationType = x.OperationType,
                    Comment = x.Comment,
                    UserId = x.UserId,
                    ProjectId = x.ProjectId,
                    TaskId = x.TaskId
                });
            }
        }

        // 4. Equipment
        var equips = await _api.GetAllEquipmentAsync();
        if (equips is not null)
        {
            foreach (var eq in equips)
            {
                var existingEq = await db.Equipments.FindAsync(eq.Id);
                if (existingEq is null)
                {
                    db.Equipments.Add(new LocalEquipment
                    {
                        Id = eq.Id,
                        Name = eq.Name,
                        Description = eq.Description,
                        CategoryId = eq.CategoryId,
                        CategoryName = eq.CategoryName,
                        ImagePath = eq.ImagePath,
                        Status = ResolvePulledEquipmentStatus(eq.Status, eq.Condition),
                        Condition = eq.Condition,
                        InventoryNumber = eq.InventoryNumber,
                        CreatedAt = eq.CreatedAt,
                        UpdatedAt = eq.UpdatedAt,
                        CheckedOutProjectId = eq.CheckedOutProjectId,
                        CheckedOutTaskId = eq.CheckedOutTaskId,
                        IsWrittenOff = eq.IsWrittenOff,
                        WrittenOffAt = eq.WrittenOffAt,
                        WrittenOffComment = eq.WrittenOffComment,
                        IsArchived = eq.IsArchived,
                        IsSynced = true
                    });
                }
                else
                {
                    if (existingEq.IsSynced)
                    {
                        existingEq.Name = eq.Name;
                        existingEq.Description = eq.Description;
                        existingEq.CategoryId = eq.CategoryId;
                        existingEq.CategoryName = eq.CategoryName;
                        existingEq.ImagePath = eq.ImagePath;
                        existingEq.Status = ResolvePulledEquipmentStatus(eq.Status, eq.Condition);
                        existingEq.Condition = eq.Condition;
                        existingEq.InventoryNumber = eq.InventoryNumber;
                        existingEq.UpdatedAt = eq.UpdatedAt;
                        existingEq.CheckedOutProjectId = eq.CheckedOutProjectId;
                        existingEq.CheckedOutTaskId = eq.CheckedOutTaskId;
                        existingEq.IsWrittenOff = eq.IsWrittenOff;
                        existingEq.WrittenOffAt = eq.WrittenOffAt;
                        existingEq.WrittenOffComment = eq.WrittenOffComment;
                        existingEq.IsArchived = eq.IsArchived;
                        existingEq.IsSynced = true;
                    }
                }
            }

            var serverEquipIds = equips.Select(e => e.Id).ToHashSet();
            var orphanEquipIds = await db.Equipments
                .Where(e => e.IsSynced && !serverEquipIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync();
            foreach (var eid in orphanEquipIds)
                await LocalDbGraphDeletion.PermanentlyDeleteEquipmentGraphAsync(db, eid);
        }

        // 5. Equipment history
        var eqHistory = await _api.GetAllEquipmentHistoryAsync();
        if (eqHistory is not null)
        {
            await db.EquipmentHistoryEntries.ExecuteDeleteAsync();
            foreach (var h in eqHistory)
            {
                db.EquipmentHistoryEntries.Add(new LocalEquipmentHistoryEntry
                {
                    Id = h.Id,
                    EquipmentId = h.EquipmentId,
                    OccurredAt = h.OccurredAt,
                    EventType = h.EventType,
                    PreviousStatus = h.PreviousStatus,
                    NewStatus = h.NewStatus,
                    ProjectId = h.ProjectId,
                    TaskId = h.TaskId,
                    UserId = h.UserId,
                    Comment = h.Comment
                });
            }
        }
    }

    public async Task<bool> PushAsync(LocalDbContext db, PendingOperation op)
    {
        return op.EntityType switch
        {
            "Material" => await SyncMaterialAsync(db, op),
            "MaterialCategory" => await SyncMaterialCategoryAsync(db, op),
            "EquipmentCategory" => await SyncEquipmentCategoryAsync(db, op),
            "MaterialStockMovement" => await SyncMaterialStockMovementAsync(db, op),
            "Equipment" => await SyncEquipmentAsync(db, op),
            "EquipmentHistory" => await SyncEquipmentHistoryAsync(db, op),
            _ => false
        };
    }

    private async Task<bool> SyncMaterialAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteMaterialAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateMaterialRequest>(op.Payload, _jsonOptions);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateMaterialAsync(req);
            if (created is not null)
            {
                var local = await db.Materials.FindAsync(op.EntityId);
                if (local is not null) local.IsSynced = true;
                return true;
            }
            return false;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateMaterialRequest>(op.Payload, _jsonOptions);
        if (updateReq is not null && await _api.UpdateMaterialAsync(op.EntityId, updateReq) is not null)
        {
            var local = await db.Materials.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            return true;
        }
        return false;
    }

    private async Task<bool> SyncMaterialCategoryAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateMaterialCategoryRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateMaterialCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncEquipmentCategoryAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<CreateEquipmentCategoryRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        req = req with { Id = op.EntityId };
        return await _api.CreateEquipmentCategoryAsync(req) is not null;
    }

    private async Task<bool> SyncMaterialStockMovementAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordMaterialStockRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        return await _api.RecordMaterialStockMovementAsync(op.EntityId, req) is not null;
    }

    private async Task<bool> SyncEquipmentAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType == SyncOperation.Delete)
            return await _api.DeleteEquipmentAsync(op.EntityId);

        if (op.OperationType == SyncOperation.Create)
        {
            var req = JsonSerializer.Deserialize<CreateEquipmentRequest>(op.Payload, _jsonOptions);
            if (req is null) return false;
            req = req with { Id = op.EntityId };
            var created = await _api.CreateEquipmentAsync(req);
            if (created is null) return false;
            
            var local = await db.Equipments.FindAsync(op.EntityId);
            if (local is not null) local.IsSynced = true;
            
            return true;
        }

        var updateReq = JsonSerializer.Deserialize<UpdateEquipmentRequest>(op.Payload, _jsonOptions);
        if (updateReq is null) return false;
        var updated = await _api.UpdateEquipmentAsync(op.EntityId, updateReq);
        if (updated is null) return false;
        
        var local2 = await db.Equipments.FindAsync(op.EntityId);
        if (local2 is not null) local2.IsSynced = true;
        
        return true;
    }

    private async Task<bool> SyncEquipmentHistoryAsync(LocalDbContext db, PendingOperation op)
    {
        if (op.OperationType != SyncOperation.Create) return true;
        var req = JsonSerializer.Deserialize<RecordEquipmentEventRequest>(op.Payload, _jsonOptions);
        if (req is null) return false;
        return await _api.RecordEquipmentEventAsync(op.EntityId, req) is not null;
    }

    // Helper methods moved from SyncService
    private static string NormalizeEquipmentStatus(string? status) => status switch
    {
        "3" => "Unavailable",
        _ => status ?? "Available"
    };

    private static bool IsUnavailableCondition(string? condition) =>
        condition is "NeedsMaintenance" or "Faulty";

    private static string ResolvePulledEquipmentStatus(string? status, string? condition)
    {
        var normalizedStatus = NormalizeEquipmentStatus(status);
        if (normalizedStatus is "InUse" or "CheckedOut" or "Retired")
            return normalizedStatus;

        return IsUnavailableCondition(condition) ? "Unavailable" : "Available";
    }
}
