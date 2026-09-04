using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace K162.App.Controls;

/// <summary>
/// The design's signature shape: a bordered panel with the top-left and bottom-right
/// corners cut at 45° (equivalent of the prototype's corner-cut clip-path).
/// </summary>
public sealed class CutCornerBorder : Decorator
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(CutCornerBorder),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(CutCornerBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(CutCornerBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CutSizeProperty = DependencyProperty.Register(
        nameof(CutSize), typeof(double), typeof(CutCornerBorder),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding), typeof(Thickness), typeof(CutCornerBorder),
        new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double CutSize { get => (double)GetValue(CutSizeProperty); set => SetValue(CutSizeProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var pad = Padding;
        if (Child is null) return new Size(pad.Left + pad.Right, pad.Top + pad.Bottom);
        var inner = new Size(
            Math.Max(0, constraint.Width - pad.Left - pad.Right),
            Math.Max(0, constraint.Height - pad.Top - pad.Bottom));
        Child.Measure(inner);
        return new Size(
            Child.DesiredSize.Width + pad.Left + pad.Right,
            Child.DesiredSize.Height + pad.Top + pad.Bottom);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var pad = Padding;
        Child?.Arrange(new Rect(pad.Left, pad.Top,
            Math.Max(0, arrangeSize.Width - pad.Left - pad.Right),
            Math.Max(0, arrangeSize.Height - pad.Top - pad.Bottom)));
        return arrangeSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var cut = Math.Min(CutSize, Math.Min(w, h) / 2);
        var half = StrokeThickness / 2;

        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(new Point(cut + half, half), isFilled: true, isClosed: true);
            gc.LineTo(new Point(w - half, half), true, false);
            gc.LineTo(new Point(w - half, h - cut - half), true, false);
            gc.LineTo(new Point(w - cut - half, h - half), true, false);
            gc.LineTo(new Point(half, h - half), true, false);
            gc.LineTo(new Point(half, cut + half), true, false);
        }
        geometry.Freeze();
        var pen = Stroke is null ? null : new Pen(Stroke, StrokeThickness);
        dc.DrawGeometry(Fill, pen, geometry);
    }
}
