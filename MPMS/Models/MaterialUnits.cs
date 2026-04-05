namespace MPMS.Models;

/// <summary>Predefined list of material units with integer/decimal flag.</summary>
public static class MaterialUnits
{
    public record UnitInfo(string Display, string Short, bool IsInteger);

    public static readonly IReadOnlyList<UnitInfo> All =
    [
        // Discrete (integer only)
        new("Штуки",            "шт",   true),
        new("Упаковки",         "упак", true),
        new("Рулоны",           "рул",  true),
        new("Листы",            "лист", true),
        new("Комплекты",        "компл",true),
        new("Пары",             "пар",  true),
        new("Бухты",            "бухт", true),
        new("Мешки",            "мешк", true),

        // Continuous (decimal)
        new("Метры",            "м",    false),
        new("Квадратные метры", "м²",   false),
        new("Кубические метры", "м³",   false),
        new("Погонные метры",   "п.м.", false),
        new("Килограммы",       "кг",   false),
        new("Граммы",           "г",    false),
        new("Тонны",            "т",    false),
        new("Литры",            "л",    false),
        new("Миллилитры",       "мл",   false),
    ];

    /// <summary>Returns true when the unit only accepts whole numbers (штуки, упаковки, etc.).</summary>
    public static bool IsIntegerUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return All.Any(u =>
            string.Equals(u.Short, unit, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Display, unit, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Tries to parse a quantity string respecting unit type.
    /// Returns null when the value is invalid for the given unit.</summary>
    public static decimal? ParseQuantity(string text, string? unit)
    {
        var normalised = text.Replace(',', '.').Trim();
        if (!decimal.TryParse(normalised,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value))
            return null;

        if (IsIntegerUnit(unit) && value != Math.Floor(value))
            return null;

        return value;
    }
}
