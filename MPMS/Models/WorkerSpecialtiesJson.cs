using System.Text.Json;

namespace MPMS.Models;

/// <summary>Serializes additional worker specialties as JSON array in DB.</summary>
public static class WorkerSpecialtiesJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string? Serialize(IEnumerable<string>? items)
    {
        if (items is null) return null;
        var clean = items
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return clean.Count == 0 ? null : JsonSerializer.Serialize(clean, Options);
    }

    public static List<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json, Options);
            return list?
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .Select(s => s.Trim())
                       .Distinct(StringComparer.Ordinal)
                       .ToList()
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>One line under name for workers: main + optional "также: …".</summary>
    public static string FormatWorkerLine(string? mainSubRole, string? additionalJson)
    {
        var extras = Deserialize(additionalJson);
        extras.RemoveAll(e => string.Equals(e, mainSubRole?.Trim(), StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(mainSubRole))
            return extras.Count > 0 ? string.Join(" · ", extras) : "Работник";
        if (extras.Count == 0) return mainSubRole.Trim();
        return $"{mainSubRole.Trim()} · также: {string.Join(", ", extras)}";
    }

    /// <summary>Под именем: основная специальность; при доп. — « · +N».</summary>
    public static string FormatWorkerLineCompact(string? mainSubRole, string? additionalJson)
    {
        var extras = Deserialize(additionalJson);
        extras.RemoveAll(e => string.Equals(e, mainSubRole?.Trim(), StringComparison.OrdinalIgnoreCase));
        var n = extras.Count;
        var extraSuffix = n > 0 ? $" · +{n}" : "";
        if (string.IsNullOrWhiteSpace(mainSubRole))
            return n > 0 ? $"Работник{extraSuffix}" : "Работник";
        return n == 0 ? mainSubRole.Trim() : $"{mainSubRole.Trim()}{extraSuffix}";
    }

    /// <summary>Стабильный хэш строки (не <see cref="string.GetHashCode"/>).</summary>
    public static int StableSpecHash(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return 0;
        unchecked
        {
            var h = 17;
            foreach (var c in spec.Trim())
                h = h * 31 + c;
            return h;
        }
    }

    /// <summary>Ключ специальности для цвета подписи/плашки: основа или первая из доп., иначе «Работник».</summary>
    public static string PrimaryDisplaySpecForColor(string? mainSubRole, string? additionalJson)
    {
        var m = mainSubRole?.Trim();
        if (!string.IsNullOrEmpty(m)) return m;
        foreach (var e in Deserialize(additionalJson))
        {
            var t = e.Trim();
            if (t.Length > 0) return t;
        }

        return "Работник";
    }

    /// <summary>#RRGGBB для цвета строки работника в списках (привязка с HexToBrush).</summary>
    public static string ForegroundHexForWorkerLine(string? mainSubRole, string? additionalJson)
    {
        var fg = BadgeForegroundRgbForSpecName(PrimaryDisplaySpecForColor(mainSubRole, additionalJson));
        return $"#{fg.R:X2}{fg.G:X2}{fg.B:X2}";
    }

    /// <summary>Канонический порядок специальностей (форма пользователя, комбо). У каждой свой фиксированный цвет.</summary>
    public static readonly string[] CanonicalWorkerSpecialties =
    [
        "Электромонтажник",
        "Сантехник",
        "Монтажник вентиляции",
        "Монтажник металлоконструкций",
        "Монтажник трубопроводов",
        "Сварщик",
        "Газосварщик",
        "Каменщик",
        "Плотник-столяр",
        "Кровельщик",
        "Штукатур-маляр",
        "Монтажник слаботочных систем",
        "Арматурщик",
        "Бетонщик",
        "Изолировщик",
        "Монтажник лесов",
        "Оператор крана",
        "Монтажник фасадов",
        "Маляр по металлу",
        "Водитель спецтехники",
        "Промышленный альпинист",
        "Монтажник опалубки",
        "Помощник монтажника",
        "Плиточник-облицовщик",
        "Токарь-слесарь",
    ];

    private const int PaletteSize = 160;
    private static readonly (byte R, byte G, byte B)[] SpecBgPalette = BuildPastelBackgrounds(PaletteSize);
    private static readonly (byte R, byte G, byte B)[] SpecFgPalette = BuildAccentForegrounds(PaletteSize);

    /// <summary>Пастельный фон: золотой угол по оттенку, единая насыщенность и светлота.</summary>
    private static (byte R, byte G, byte B)[] BuildPastelBackgrounds(int count)
    {
        var arr = new (byte R, byte G, byte B)[count];
        for (var i = 0; i < count; i++)
        {
            var hue = i * 137.50845786834786 % 360.0;
            arr[i] = HslToRgbByte(hue, 0.30, 0.935);
        }

        return arr;
    }

    /// <summary>Текст/акцент: тот же оттенок, выше насыщенность, ниже светлота.</summary>
    private static (byte R, byte G, byte B)[] BuildAccentForegrounds(int count)
    {
        var arr = new (byte R, byte G, byte B)[count];
        for (var i = 0; i < count; i++)
        {
            var hue = i * 137.50845786834786 % 360.0;
            arr[i] = HslToRgbByte(hue, 0.76, 0.31);
        }

        return arr;
    }

    private static (byte R, byte G, byte B) HslToRgbByte(double hDeg, double s, double l)
    {
        var h = hDeg / 360.0;
        double r, g, b;
        if (s <= 0.0001)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }

        return (
            (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255),
            (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static int PaletteIndexForSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return 0;
        var t = spec.Trim();
        if (string.Equals(t, "Работник", StringComparison.OrdinalIgnoreCase))
            return CanonicalWorkerSpecialties.Length;

        for (var i = 0; i < CanonicalWorkerSpecialties.Length; i++)
        {
            if (string.Equals(t, CanonicalWorkerSpecialties[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        var reserved = CanonicalWorkerSpecialties.Length + 1;
        var h = StableSpecHash(t);
        return reserved + (Math.Abs(h) % (PaletteSize - reserved));
    }

    /// <summary>RGB фона плашки по названию специальности (то же имя — тот же цвет).</summary>
    public static (byte R, byte G, byte B) BadgeBackgroundRgbForSpecName(string? spec) =>
        SpecBgPalette[PaletteIndexForSpec(spec)];

    /// <summary>RGB текста плашки по названию специальности.</summary>
    public static (byte R, byte G, byte B) BadgeForegroundRgbForSpecName(string? spec) =>
        SpecFgPalette[PaletteIndexForSpec(spec)];

    /// <summary>RGB для круга-аватара в списке (насыщенный акцент).</summary>
    public static (byte R, byte G, byte B) PickerAvatarRgbForSpecName(string? spec) =>
        BadgeForegroundRgbForSpecName(spec);
}
