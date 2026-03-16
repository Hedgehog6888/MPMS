using SkiaSharp;

namespace MPMS.API.Services;

/// <summary>
/// Generates initials-based avatars for users (server-side, no WPF dependency).
/// </summary>
public static class AvatarGenerator
{
    private static readonly string[] PaletteColors =
    {
        "#1B6EC2", "#C0392B", "#27AE60", "#8E44AD",
        "#E67E22", "#16A085", "#2980B9", "#D35400",
        "#1ABC9C", "#9B59B6", "#E74C3C", "#2ECC71"
    };

    public static string GetColorForName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "#1B6EC2";
        int hash = 0;
        foreach (char c in name) hash = hash * 31 + c;
        return PaletteColors[Math.Abs(hash) % PaletteColors.Length];
    }

    public static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}"
            : char.ToUpper(name[0]).ToString();
    }

    /// <summary>Generates PNG bytes for an initials avatar. Works on any thread.</summary>
    public static byte[] GenerateInitialsAvatar(string name, string? hexColor = null, int size = 256)
    {
        hexColor ??= GetColorForName(name);
        var initials = GetInitials(name);

        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var bgColor = SKColor.Parse(hexColor);
        using (var paint = new SKPaint { Color = bgColor, IsAntialias = true })
            canvas.DrawCircle(size / 2f, size / 2f, size / 2f, paint);

        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), size * 0.5f);
        using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var width = font.MeasureText(initials);
        var metrics = font.Metrics;
        var textHeight = metrics.Descent - metrics.Ascent;
        var baselineY = size / 2f - textHeight / 2f - metrics.Ascent;
        var x = (size - width) / 2f;
        canvas.DrawText(initials, x, baselineY, SKTextAlign.Left, font, textPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        return ms.ToArray();
    }
}
