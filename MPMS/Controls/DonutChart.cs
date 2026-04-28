using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MPMS.Controls;

/// <summary>
/// A simple donut/pie chart control drawn using WPF Path geometry.
/// Bind Segments to a list of DonutSegment items.
/// </summary>
public class DonutChart : Canvas
{
    public static readonly DependencyProperty SegmentsProperty =
        DependencyProperty.Register(nameof(Segments), typeof(IList<DonutSegment>),
            typeof(DonutChart), new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender, OnSegmentsChanged));

    public static readonly DependencyProperty InnerRadiusRatioProperty =
        DependencyProperty.Register(nameof(InnerRadiusRatio), typeof(double),
            typeof(DonutChart), new FrameworkPropertyMetadata(0.55,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IList<DonutSegment>? Segments
    {
        get => (IList<DonutSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double InnerRadiusRatio
    {
        get => (double)GetValue(InnerRadiusRatioProperty);
        set => SetValue(InnerRadiusRatioProperty, value);
    }

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as DonutChart)?.InvalidateVisual();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double outerRadius = Math.Min(w, h) / 2.0;
        double innerRadius = outerRadius * InnerRadiusRatio;
        var center = new Point(w / 2.0, h / 2.0);

        var segs = Segments;
        double total = segs?.Sum(s => s.Value) ?? 0;

        if (segs is null || segs.Count == 0 || total <= 0)
        {
            var geometry = CreateDonutSlice(center, outerRadius, innerRadius, 0, 359.99);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(241, 245, 249)), null, geometry);
            return;
        }

        double startAngle = -90.0; // start from top

        foreach (var seg in segs)
        {
            if (seg.Value <= 0) continue;
            double sweepAngle = 360.0 * seg.Value / total;

            // Cap at 359.99 to prevent full circle degenerate case
            if (sweepAngle >= 360) sweepAngle = 359.99;

            var geometry = CreateDonutSlice(center, outerRadius, innerRadius, startAngle, sweepAngle);
            dc.DrawGeometry(new SolidColorBrush(seg.Color), null, geometry);

            startAngle += sweepAngle;
        }
    }

    private static Geometry CreateDonutSlice(Point center, double outerR, double innerR,
        double startAngleDeg, double sweepAngleDeg)
    {
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad   = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;
        bool largeArc   = sweepAngleDeg > 180;

        var outerStart = new Point(center.X + outerR * Math.Cos(startRad),
                                   center.Y + outerR * Math.Sin(startRad));
        var outerEnd   = new Point(center.X + outerR * Math.Cos(endRad),
                                   center.Y + outerR * Math.Sin(endRad));
        var innerStart = new Point(center.X + innerR * Math.Cos(startRad),
                                   center.Y + innerR * Math.Sin(startRad));
        var innerEnd   = new Point(center.X + innerR * Math.Cos(endRad),
                                   center.Y + innerR * Math.Sin(endRad));

        var figure = new PathFigure { StartPoint = outerStart };
        figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerR, outerR), 0,
            largeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(innerEnd, true));
        figure.Segments.Add(new ArcSegment(innerStart, new Size(innerR, innerR), 0,
            largeArc, SweepDirection.Counterclockwise, true));
        figure.IsClosed = true;

        return new PathGeometry(new[] { figure });
    }
}

public class DonutSegment
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
    public Color Color { get; set; } = Colors.Gray;
    public string ColorHex
    {
        get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
        set => Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
    }
}
