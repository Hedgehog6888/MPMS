namespace MPMS.API.Models;

public enum MaterialStockOperationType
{
    Purchase = 0,
    Consumption = 1,
    Adjustment = 2,
    WriteOff = 3,
    ReturnToStock = 4
}

public enum EquipmentStatus
{
    Available = 0,
    InUse = 1,
    Retired = 2,
    Unavailable = 3
}

public enum EquipmentCondition
{
    Good = 0,
    NeedsMaintenance = 1,
    Faulty = 2
}

public enum EquipmentHistoryEventType
{
    CheckedOut = 0,
    Returned = 1,
    StatusChanged = 2,
    Note = 3,
    WrittenOff = 4
}
