using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MPMS.Controls;

public partial class CircularProgressRing : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularProgressRing),
            new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty RingSizeProperty =
        DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(CircularProgressRing),
            new PropertyMetadata(36d, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(CircularProgressRing),
            new PropertyMetadata(3d, OnVisualPropertyChanged));

    public static readonly DependencyProperty ProgressBrushProperty =
        DependencyProperty.Register(nameof(ProgressBrush), typeof(Brush), typeof(CircularProgressRing),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty CenterContentProperty =
        DependencyProperty.Register(nameof(CenterContent), typeof(object), typeof(CircularProgressRing));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double RingSize
    {
        get => (double)GetValue(RingSizeProperty);
        set => SetValue(RingSizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush ProgressBrush
    {
        get => (Brush)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public object? CenterContent
    {
        get => GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public CircularProgressRing()
    {
        InitializeComponent();
        if (ProgressBrush == null)
            ProgressBrush = (Brush)Application.Current.FindResource("PrimaryBrush");
        Loaded += (_, _) => UpdateArc();
        SizeChanged += (_, _) => UpdateArc();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircularProgressRing ring)
            ring.UpdateArc();
    }

    private void UpdateArc()
    {
        var radius = (RingSize - StrokeThickness) / 2.0;
        var circumference = 2 * Math.PI * radius;
        var clamped = Math.Clamp(Value, 0, 100);
        var filled = circumference * clamped / 100.0;
        var empty = circumference - filled;
        var dash = filled / StrokeThickness;
        var gap = empty / StrokeThickness;

        // Второй сегмент = полная длина окружности, чтобы штрих не повторялся на половине круга.
        ProgressEllipse.Opacity = clamped <= 0 ? 0 : 1;
        ProgressEllipse.StrokeDashArray = clamped >= 99.9
            ? new DoubleCollection([circumference / StrokeThickness])
            : new DoubleCollection([dash, gap]);
    }
}
