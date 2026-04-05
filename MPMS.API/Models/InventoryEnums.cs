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
    CheckedOut = 1,
    InMaintenance = 2,
    Retired = 3
}

public enum EquipmentHistoryEventType
{
    CheckedOut = 0,
    Returned = 1,
    StatusChanged = 2,
    Note = 3
}
