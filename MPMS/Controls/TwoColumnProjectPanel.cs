using System.Windows;
using System.Windows.Controls;

namespace MPMS.Controls;

/// <summary>
/// Панель для сетки карточек проектов: по 2 в ряд, последняя нечётная — на всю ширину.
/// </summary>
public class TwoColumnProjectPanel : Panel
{
    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(TwoColumnProjectPanel),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (double.IsInfinity(width) || double.IsNaN(width) || width <= 0)
            width = 0;

        var count = InternalChildren.Count;
        if (count == 0)
            return new Size(width, 0);

        var halfWidth = width > 0 ? Math.Max(0, (width - ColumnSpacing) / 2) : 0;
        var totalHeight = 0d;
        var maxWidth = 0d;

        for (var i = 0; i < count;)
        {
            var isLonely = count % 2 == 1 && i == count - 1;
            var child = InternalChildren[i];
            var childWidth = isLonely ? width : halfWidth;
            child.Measure(new Size(childWidth, availableSize.Height));
            var size = GetDesiredSizeWithMargin(child);

            if (isLonely)
            {
                totalHeight += size.Height;
                maxWidth = Math.Max(maxWidth, size.Width);
                i++;
                continue;
            }

            var next = InternalChildren[i + 1];
            next.Measure(new Size(halfWidth, availableSize.Height));
            var nextSize = GetDesiredSizeWithMargin(next);
            totalHeight += Math.Max(size.Height, nextSize.Height);
            maxWidth = Math.Max(maxWidth, size.Width + ColumnSpacing + nextSize.Width);
            i += 2;
        }

        return new Size(Math.Max(width, maxWidth), totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
            return finalSize;

        var halfWidth = finalSize.Width > 0 ? Math.Max(0, (finalSize.Width - ColumnSpacing) / 2) : 0;
        var y = 0d;

        for (var i = 0; i < count;)
        {
            var isLonely = count % 2 == 1 && i == count - 1;

            if (isLonely)
            {
                var child = InternalChildren[i];
                var h = GetDesiredSizeWithMargin(child).Height;
                ArrangeChild(child, new Rect(0, y, finalSize.Width, h));
                y += h;
                i++;
                continue;
            }

            var left = InternalChildren[i];
            var right = InternalChildren[i + 1];
            var leftH = GetDesiredSizeWithMargin(left).Height;
            var rightH = GetDesiredSizeWithMargin(right).Height;
            var rowH = Math.Max(leftH, rightH);

            ArrangeChild(left, new Rect(0, y, halfWidth, rowH));
            ArrangeChild(right, new Rect(halfWidth + ColumnSpacing, y, halfWidth, rowH));
            y += rowH;
            i += 2;
        }

        return finalSize;
    }

    private static Size GetDesiredSizeWithMargin(UIElement element)
    {
        if (element is not FrameworkElement fe)
            return element.DesiredSize;

        var m = fe.Margin;
        return new Size(
            element.DesiredSize.Width + m.Left + m.Right,
            element.DesiredSize.Height + m.Top + m.Bottom);
    }

    private static void ArrangeChild(UIElement element, Rect slot)
    {
        if (element is not FrameworkElement fe)
        {
            element.Arrange(slot);
            return;
        }

        var m = fe.Margin;
        element.Arrange(new Rect(
            slot.Left + m.Left,
            slot.Top + m.Top,
            Math.Max(0, slot.Width - m.Left - m.Right),
            Math.Max(0, slot.Height - m.Top - m.Bottom)));
    }
}
