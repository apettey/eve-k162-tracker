using System.Globalization;
using System.Text;
using System.Windows.Data;
using System.Windows.Media;
using K162.Core;

namespace K162.App;

/// <summary>ThreatLevel to its accent color (calm green / warm amber / hot red).</summary>
public sealed class ThreatToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Calm = Frozen("#FF3ECF7A");
    public static readonly SolidColorBrush Warm = Frozen("#FFD6A52A");
    public static readonly SolidColorBrush Hot = Frozen("#FFE0563C");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ThreatLevel level ? level switch
        {
            ThreatLevel.Hot => Hot,
            ThreatLevel.Warm => Warm,
            _ => Calm,
        } : Calm;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Emulates CSS letter-spacing (which WPF lacks) by interleaving hair spaces (U+200A):
/// parameter = number of hair spaces to insert between characters.
/// </summary>
public sealed class TrackingConverter : IValueConverter
{
    private const char HairSpace = (char)0x200A;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        var count = int.TryParse(parameter?.ToString(), out var n) ? n : 1;
        if (text.Length < 2 || count <= 0) return text;
        var spacer = new string(HairSpace, count);
        var sb = new StringBuilder(text.Length * (count + 1));
        for (var i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (i < text.Length - 1) sb.Append(spacer);
        }
        return sb.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
