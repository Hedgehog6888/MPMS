using System.IO;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

/// <summary>
/// Utility class for generating and managing user avatars.
/// Avatars are stored as PNG byte arrays in the database.
/// </summary>
public static class AvatarHelper
{
    private static readonly string[] PaletteColors =
    {
        "#1B6EC2", "#C0392B", "#27AE60", "#8E44AD",
        "#E67E22", "#16A085", "#2980B9", "#D35400",
        "#1ABC9C", "#9B59B6", "#E74C3C", "#2ECC71"
    };

    /// <summary>Returns a deterministic accent color for a given display name.</summary>
    public static string GetColorForName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "#1B6EC2";
        int hash = 0;
        foreach (char c in name) hash = hash * 31 + c;
        return PaletteColors[Math.Abs(hash) % PaletteColors.Length];
    }

    /// <summary>Extracts up to 2 initials from a full name (e.g. "Иван Петров" → "ИП").</summary>
    public static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}"
            : char.ToUpper(name[0]).ToString();
    }

    /// <summary>
    /// Generates an initials-based avatar as a PNG byte array.
    /// Must be called on the UI thread (uses WPF DrawingVisual).
    /// </summary>
    public static byte[] GenerateInitialsAvatar(string name, string? hexColor = null, int size = 256)
    {
        hexColor ??= GetColorForName(name);
        var initials = GetInitials(name);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            Color bg;
            try { bg = (Color)ColorConverter.ConvertFromString(hexColor); }
            catch { bg = Color.FromRgb(0x1B, 0x6E, 0xC2); }

            double cx = size / 2.0;
            double cy = size / 2.0;
            double r  = size / 2.0;

            dc.DrawEllipse(new SolidColorBrush(bg), null, new Point(cx, cy), r, r);

            double fontSize = size * 0.5;
            var ft = new FormattedText(
                initials,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily("Segoe UI"),
                    FontStyles.Normal,
                    FontWeights.Bold,
                    FontStretches.Normal),
                fontSize,
                Brushes.White,
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);

            double x = (size - ft.Width) / 2;
            double y = (size - ft.Height) / 2;
            dc.DrawText(ft, new Point(x, y));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Converts PNG byte array to a frozen BitmapImage for WPF display. Returns null if data is invalid.</summary>
    public static BitmapImage? BytesToBitmapImage(byte[]? data)
    {
        if (data is null || data.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Reads an image file and returns its bytes. Returns null if file is missing or unreadable.</summary>
    public static byte[]? FileToBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    /// <summary>
    /// Returns the best available ImageSource for a user:
    /// 1. AvatarData (bytes from DB) if present
    /// 2. AvatarPath (file path) if file exists
    /// 3. Generated initials avatar if fallbackDisplayName is provided (must be on UI thread)
    /// 4. null (caller should render initials circle)
    /// </summary>
    public static BitmapImage? GetImageSource(byte[]? avatarData, string? avatarPath, string? fallbackDisplayName = null)
    {
        if (avatarData is { Length: > 0 })
            return BytesToBitmapImage(avatarData);

        if (!string.IsNullOrWhiteSpace(avatarPath) && File.Exists(avatarPath))
        {
            var bytes = FileToBytes(avatarPath);
            if (bytes is not null)
                return BytesToBitmapImage(bytes);
        }

        if (!string.IsNullOrWhiteSpace(fallbackDisplayName))
        {
            var bytes = GenerateInitialsAvatar(fallbackDisplayName);
            return BytesToBitmapImage(bytes);
        }

        return null;
    }

}
