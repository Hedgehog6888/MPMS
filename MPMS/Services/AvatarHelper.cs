using System.IO;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

public static class AvatarHelper
{
    private static readonly string[] PaletteColors =
    {
        "#1ABC9C", "#2ECC71", "#3498DB", "#9B59B6", "#34495E",
        "#F1C40F", "#E67E22", "#E74C3C", "#95A5A6", "#D35400"
    };

    public static string GetColorForName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "#34495E";
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

    public static byte[] GenerateInitialsAvatar(string name, string? hexColor = null, int size = 256)
    {
        hexColor ??= GetColorForName(name);
        var initials = GetInitials(name);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            Color bg;
            try { bg = (Color)ColorConverter.ConvertFromString(hexColor); }
            catch { bg = Color.FromRgb(0x34, 0x49, 0x5E); }

            double cx = size / 2.0;
            double cy = size / 2.0;
            double r = size / 2.0;

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

    public static BitmapImage? BytesToBitmapImage(byte[]? data, int decodeWidth = 0)
    {
        if (data is null || data.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.CacheOption = BitmapCacheOption.OnLoad;

            if (decodeWidth > 0)
            {
                bmp.DecodePixelWidth = decodeWidth;
            }

            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public static byte[]? FileToBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    public static BitmapImage? GetImageSource(byte[]? avatarData, string? avatarPath, string? fallbackDisplayName = null, int decodeWidth = 0)
    {
        if (avatarData is { Length: > 0 })
            return BytesToBitmapImage(avatarData, decodeWidth);

        if (!string.IsNullOrWhiteSpace(avatarPath) && File.Exists(avatarPath))
        {
            var bytes = FileToBytes(avatarPath);
            if (bytes is not null)
                return BytesToBitmapImage(bytes, decodeWidth);
        }

        if (!string.IsNullOrWhiteSpace(fallbackDisplayName))
        {
            var bytes = GenerateInitialsAvatar(fallbackDisplayName);
            return BytesToBitmapImage(bytes, decodeWidth);
        }

        return null;
    }

}
