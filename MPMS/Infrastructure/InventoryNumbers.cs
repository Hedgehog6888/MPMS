using Microsoft.EntityFrameworkCore;
using MPMS.Data;

namespace MPMS.Infrastructure;

/// <summary>Сквозная нумерация инвентарных номеров для локальной БД (MAT- / EQP-).</summary>
public static class InventoryNumbers
{
    public const string MaterialPrefix = "MAT-";
    public const string EquipmentPrefix = "EQP-";

    /// <summary>Следующий свободный номер. Если <paramref name="preferred"/> задан и не занят — возвращает его.</summary>
    public static async Task<string> NextMaterialAsync(LocalDbContext db, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var p = preferred.Trim();
            var taken = await db.Materials.AnyAsync(m => m.InventoryNumber == p);
            if (!taken) return p;
        }

        var max = await MaxMaterialSequenceAsync(db);
        return $"{MaterialPrefix}{(max + 1):D6}";
    }

    public static async Task<string> NextEquipmentAsync(LocalDbContext db, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var p = preferred.Trim();
            var taken = await db.Equipments.AnyAsync(e => e.InventoryNumber == p);
            if (!taken) return p;
        }

        var max = await MaxEquipmentSequenceAsync(db);
        return $"{EquipmentPrefix}{(max + 1):D6}";
    }

    private static async Task<int> MaxMaterialSequenceAsync(LocalDbContext db)
    {
        var nums = await db.Materials
            .Where(m => m.InventoryNumber != null && m.InventoryNumber.StartsWith(MaterialPrefix))
            .Select(m => m.InventoryNumber!)
            .ToListAsync();
        return MaxSuffixAfterPrefix(nums, MaterialPrefix);
    }

    private static async Task<int> MaxEquipmentSequenceAsync(LocalDbContext db)
    {
        var nums = await db.Equipments
            .Where(e => e.InventoryNumber != null && e.InventoryNumber.StartsWith(EquipmentPrefix))
            .Select(e => e.InventoryNumber!)
            .ToListAsync();
        return MaxSuffixAfterPrefix(nums, EquipmentPrefix);
    }

    private static int MaxSuffixAfterPrefix(IEnumerable<string> values, string prefix)
    {
        var pl = prefix.Length;
        var max = 0;
        foreach (var s in values)
        {
            if (s.Length <= pl) continue;
            if (!int.TryParse(s.AsSpan(pl), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                continue;
            if (n > max) max = n;
        }

        return max;
    }
}
