using System.Windows;
using System.Windows.Controls;

namespace K162.App.Controls;

/// <summary>
/// CSS-grid "repeat(auto-fit, minmax(MinItemWidth, 1fr))" equivalent: as many equal-width
/// columns as fit, each at least MinItemWidth, with a fixed gap; rows sized to their tallest item.
/// </summary>
public sealed class AutoFitPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AutoFitPanel),
        new FrameworkPropertyMetadata(430.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AutoFitPanel),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private (int Columns, double ItemWidth) Layout(double availableWidth)
    {
        var visible = InternalChildren.Cast<UIElement>().Count(c => c.Visibility != Visibility.Collapsed);
        if (visible == 0) return (1, availableWidth);
        var cols = Math.Max(1, (int)Math.Floor((availableWidth + Gap) / (MinItemWidth + Gap)));
        cols = Math.Min(cols, visible);
        var itemWidth = (availableWidth - (cols - 1) * Gap) / cols;
        return (cols, Math.Max(0, itemWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        var (cols, itemWidth) = Layout(width);
        double totalHeight = 0, rowHeight = 0;
        var col = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            if (++col == cols)
            {
                totalHeight += rowHeight + Gap;
                rowHeight = 0;
                col = 0;
            }
        }
        if (col > 0) totalHeight += rowHeight + Gap;
        if (totalHeight > 0) totalHeight -= Gap;
        return new Size(width, Math.Max(0, totalHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (cols, itemWidth) = Layout(finalSize.Width);
        var row = new List<UIElement>();
        double y = 0;
        var visibleChildren = InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed);
        foreach (var child in visibleChildren)
        {
            row.Add(child);
            if (row.Count == cols)
            {
                y += ArrangeRow(row, itemWidth, y) + Gap;
                row.Clear();
            }
        }
        if (row.Count > 0) ArrangeRow(row, itemWidth, y);
        return finalSize;
    }

    private double ArrangeRow(List<UIElement> row, double itemWidth, double y)
    {
        var rowHeight = row.Max(c => c.DesiredSize.Height);
        double x = 0;
        foreach (var child in row)
        {
            child.Arrange(new Rect(x, y, itemWidth, rowHeight));
            x += itemWidth + Gap;
        }
        return rowHeight;
    }
}
