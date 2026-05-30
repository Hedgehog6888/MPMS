using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MPMS.Models;

namespace MPMS.Infrastructure;

public static class ActivityLogStripeBrushBuilder
{
    public const double Width = 5;

    public static Brush Build(LocalActivityLog log)
    {
        var color = GetAccentColor(log);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            [
                Stop(color, 220, 0.0),
                Stop(color, 160, 0.45),
                Stop(color, 90, 0.75),
                Stop(color, 25, 1.0),
            ]
        };
        brush.Freeze();
        return brush;
    }

    private static Color GetAccentColor(LocalActivityLog log)
    {
        if (ActivityLogToAccentBrushConverter.Instance.Convert(log, typeof(Brush), null!, CultureInfo.InvariantCulture)
            is SolidColorBrush brush)
            return brush.Color;
        return Color.FromRgb(0x64, 0x74, 0x8B);
    }

    private static GradientStop Stop(Color c, byte a, double offset) =>
        new(Color.FromArgb(a, c.R, c.G, c.B), offset);
}
