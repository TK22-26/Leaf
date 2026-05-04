using System.Globalization;
using System.Windows.Data;

namespace Leaf.Converters;

/// <summary>
/// Multi-value converter that produces the pixel height of a progress
/// fill given a numerator (resolved count), a denominator (total count),
/// and the available container height. Used by
/// <c>ConflictFileTree.xaml</c>'s accent-stripe fill so the green portion
/// grows from the bottom up as conflict regions get accepted, scaled to
/// whatever vertical space the surrounding row has allocated.
/// </summary>
/// <remarks>
/// Inputs (in order):
/// <list type="number">
///   <item><description>Resolved count (int) — numerator</description></item>
///   <item><description>Total count (int) — denominator</description></item>
///   <item><description>Container ActualHeight (double) — pixel ceiling</description></item>
/// </list>
/// Returns 0 if any input is invalid (null, wrong type, total ≤ 0). The
/// result is clamped to <c>[0, ActualHeight]</c> so a transient
/// resolved &gt; total state during a property-change cascade can't paint
/// a fill that overflows the stripe.
/// </remarks>
public sealed class ProgressFillHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 3) return 0.0;
        if (values[0] is not int resolved) return 0.0;
        if (values[1] is not int total || total <= 0) return 0.0;
        if (values[2] is not double height || double.IsNaN(height) || height <= 0) return 0.0;

        var ratio = (double)resolved / total;
        if (ratio <= 0) return 0.0;
        if (ratio >= 1) return height;
        return Math.Round(height * ratio);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
