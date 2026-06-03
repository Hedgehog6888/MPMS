using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MPMS.Controls;

/// <summary>
/// Простой элемент управления donut/pie chart, рисуемый через WPF Path geometry.
/// Привяжите Segments к списку DonutSegment.
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

    public static readonly DependencyProperty HoveredSegmentIndexProperty =
        DependencyProperty.Register(nameof(HoveredSegmentIndex), typeof(int),
            typeof(DonutChart), new FrameworkPropertyMetadata(-1,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsAnySegmentHoveredProperty =
        DependencyProperty.Register(nameof(IsAnySegmentHovered), typeof(bool),
            typeof(DonutChart), new FrameworkPropertyMetadata(false));

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

    public int HoveredSegmentIndex
    {
        get => (int)GetValue(HoveredSegmentIndexProperty);
        set => SetValue(HoveredSegmentIndexProperty, value);
    }

    public bool IsAnySegmentHovered
    {
        get => (bool)GetValue(IsAnySegmentHoveredProperty);
        set => SetValue(IsAnySegmentHoveredProperty, value);
    }

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as DonutChart)?.InvalidateVisual();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        int hoveredIndex = GetSegmentAtPosition(pos);
        if (hoveredIndex != HoveredSegmentIndex)
        {
            HoveredSegmentIndex = hoveredIndex;
            IsAnySegmentHovered = (hoveredIndex != -1);
            UpdateSegmentHoverStates();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (HoveredSegmentIndex != -1)
        {
            HoveredSegmentIndex = -1;
            IsAnySegmentHovered = false;
            UpdateSegmentHoverStates();
        }
    }

    private int GetSegmentAtPosition(Point pos)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return -1;

        double outerRadius = Math.Min(w, h) / 2.0;
        double innerRadius = outerRadius * InnerRadiusRatio;
        var center = new Point(w / 2.0, h / 2.0);

        var dx = pos.X - center.X;
        var dy = pos.Y - center.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance < innerRadius || distance > outerRadius) return -1;

        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        angle = (angle + 90 + 360) % 360;

        var segs = Segments;
        double total = segs?.Sum(s => s.Value) ?? 0;
        if (segs is null || segs.Count == 0 || total <= 0) return -1;

        double startAngle = 0.0;
        for (int i = 0; i < segs.Count; i++)
        {
            if (segs[i].Value <= 0) continue;
            double sweepAngle = 360.0 * segs[i].Value / total;
            if (sweepAngle >= 360) sweepAngle = 359.99;

            if (angle >= startAngle && angle < startAngle + sweepAngle)
                return i;

            startAngle += sweepAngle;
        }

        return -1;
    }

    private void UpdateSegmentHoverStates()
    {
        var segs = Segments;
        if (segs is null) return;

        for (int i = 0; i < segs.Count; i++)
        {
            segs[i].IsHovered = (i == HoveredSegmentIndex);
        }
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

        double startAngle = -90.0;

        foreach (var seg in segs)
        {
            if (seg.Value <= 0) continue;
            double sweepAngle = 360.0 * seg.Value / total;

            if (sweepAngle >= 360) sweepAngle = 359.99;

            var geometry = CreateDonutSlice(center, outerRadius, innerRadius, startAngle, sweepAngle);
            var brush = new SolidColorBrush(seg.Color);
            dc.DrawGeometry(brush, new Pen(brush, 0.5), geometry);

            startAngle += sweepAngle;
        }
    }

    private static Geometry CreateDonutSlice(Point center, double outerR, double innerR,
        double startAngleDeg, double sweepAngleDeg)
    {
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;
        bool largeArc = sweepAngleDeg > 180;

        var outerStart = new Point(center.X + outerR * Math.Cos(startRad),
                                   center.Y + outerR * Math.Sin(startRad));
        var outerEnd = new Point(center.X + outerR * Math.Cos(endRad),
                                   center.Y + outerR * Math.Sin(endRad));
        var innerStart = new Point(center.X + innerR * Math.Cos(startRad),
                                   center.Y + innerR * Math.Sin(startRad));
        var innerEnd = new Point(center.X + innerR * Math.Cos(endRad),
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

public partial class DonutSegment : ObservableObject
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
    public Color Color { get; set; } = Colors.Gray;
    public double Percentage { get; set; }
    [ObservableProperty] private bool _isHovered;
    public string ColorHex
    {
        get => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}";
        set => Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
    }
}
