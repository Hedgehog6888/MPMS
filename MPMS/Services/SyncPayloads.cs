using MPMS.Models;

namespace MPMS.Services;

/// <summary>Полные DTO для отправки на сервер с учётом локальных флагов.</summary>
public static class SyncPayloads
{
    public static UpdateProjectRequest Project(LocalProject p) => new(
        p.Name, p.Description, p.Client, p.Address, p.StartDate, p.EndDate,
        p.Status, p.ManagerId, p.IsMarkedForDeletion, p.IsArchived);

    public static UpdateTaskRequest Task(LocalTask t) => new(
        t.Name, t.Description, t.AssignedUserId, t.Priority, t.DueDate, t.Status,
        t.IsMarkedForDeletion, t.IsArchived);

    public static UpdateStageRequest Stage(LocalTaskStage s) => new(
        s.Name, s.Description, s.AssignedUserId, s.Status, s.DueDate,
        s.IsMarkedForDeletion, s.IsArchived);

    public static UpdateMaterialRequest Material(LocalMaterial m) => new(
        m.Name, m.Unit, m.Description, m.CategoryId, m.ImagePath, m.Cost, m.InventoryNumber,
        m.IsWrittenOff, m.WrittenOffAt, m.WrittenOffComment);

    public static UpdateEquipmentRequest Equipment(LocalEquipment e) => new(
        e.Name, e.Description, e.CategoryId, e.ImagePath, e.InventoryNumber,
        Enum.TryParse<EquipmentCondition>(e.Condition, true, out var c) ? c : EquipmentCondition.Good,
        MapEquipmentStatus(e.Status),
        e.IsWrittenOff, e.WrittenOffAt, e.WrittenOffComment);

    private static EquipmentStatus? MapEquipmentStatus(string? s) => s switch
    {
        null or "" => null,
        "Available" => EquipmentStatus.Available,
        "InUse" or "CheckedOut" => EquipmentStatus.InUse,
        "Retired" or "WrittenOff" => EquipmentStatus.Retired,
        "Unavailable" or "3" => EquipmentStatus.Unavailable,
        _ => Enum.TryParse<EquipmentStatus>(s, true, out var x) ? x : null
    };
}
