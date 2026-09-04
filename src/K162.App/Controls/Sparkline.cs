using System.Windows;
using System.Windows.Media;

namespace K162.App.Controls;

/// <summary>
/// Kill-activity bar strip: one bar per bin, bottom-aligned, active bars in bright
/// accent, empty bins as faint stubs (6% height minimum) — matching the prototype.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty BinsProperty = DependencyProperty.Register(
        nameof(Bins), typeof(IReadOnlyList<int>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveBrushProperty = DependencyProperty.Register(
        nameof(ActiveBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Green, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZeroBrushProperty = DependencyProperty.Register(
        nameof(ZeroBrush), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DarkGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int>? Bins { get => (IReadOnlyList<int>?)GetValue(BinsProperty); set => SetValue(BinsProperty, value); }
    public Brush ActiveBrush { get => (Brush)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush ZeroBrush { get => (Brush)GetValue(ZeroBrushProperty); set => SetValue(ZeroBrushProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var bins = Bins;
        var w = ActualWidth;
        var h = ActualHeight;
        if (bins is null || bins.Count == 0 || w <= 0 || h <= 0) return;

        var max = Math.Max(1, bins.Max());
        var barWidth = (w - (bins.Count - 1) * Gap) / bins.Count;
        if (barWidth <= 0) return;

        for (var i = 0; i < bins.Count; i++)
        {
            var v = bins[i];
            var barHeight = Math.Max(2, Math.Max(0.06, (double)v / max) * h);
            var x = i * (barWidth + Gap);
            dc.DrawRectangle(v > 0 ? ActiveBrush : ZeroBrush, null,
                new Rect(x, h - barHeight, barWidth, barHeight));
        }
    }
}
