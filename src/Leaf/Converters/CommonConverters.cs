using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Leaf.Converters;

/// <summary>
/// Converts bool to Visibility (true = Visible, false = Collapsed).
/// Pass "Inverse" as parameter to invert the logic.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Check if we should invert
            if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = !boolValue;
            }
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            var result = visibility == Visibility.Visible;
            // Check if we should invert
            if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                result = !result;
            }
            return result;
        }
        return false;
    }
}

/// <summary>
/// Converts non-null to Visibility (non-null = Visible, null = Collapsed).
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts null to Visibility (null = Visible, non-null = Collapsed).
/// Inverse of NullToVisibilityConverter.
/// </summary>
public class NullToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts bool to Visibility with inverse logic (true = Collapsed, false = Visible).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return true;
    }
}

/// <summary>
/// Converts a bool to a GridLength. True = Star (default) or Auto, False = 0.
/// Pass "Auto" as parameter to use Auto instead of Star.
/// </summary>
public class BoolToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            if (!boolValue)
            {
                return new GridLength(0);
            }

            if (parameter is string param && param.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return GridLength.Auto;
            }

            return new GridLength(1, GridUnitType.Star);
        }

        return new GridLength(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength length)
        {
            return length.Value > 0 && length.GridUnitType != GridUnitType.Pixel;
        }

        return false;
    }
}

/// <summary>
/// Converts a bool to a row height for expandable sections.
/// True = Star, False = Auto.
/// </summary>
public class BoolToSectionRowHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        return GridLength.Auto;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is GridLength length)
        {
            return length.GridUnitType == GridUnitType.Star && length.Value > 0;
        }

        return false;
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts empty string/collection to Visibility (empty = Collapsed, non-empty = Visible).
/// </summary>
public class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.IsNullOrEmpty(str) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (value is System.Collections.ICollection collection)
        {
            return collection.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts bool to opacity for dimming effect.
/// IsDimmed = true → 0.3 (dimmed), IsDimmed = false → 1.0 (normal).
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDimmed)
        {
            return isDimmed ? 0.3 : 1.0;
        }
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts file change status to appropriate brush.
/// </summary>
public class FileStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.FileChangeStatus status)
        {
            return status switch
            {
                Models.FileChangeStatus.Added => Application.Current.FindResource("AddedBrush"),
                Models.FileChangeStatus.Deleted => Application.Current.FindResource("DeletedBrush"),
                Models.FileChangeStatus.Modified => Application.Current.FindResource("ModifiedBrush"),
                Models.FileChangeStatus.Renamed => Application.Current.FindResource("RenamedBrush"),
                Models.FileChangeStatus.Conflicted => Application.Current.FindResource("ConflictBrush"),
                _ => Application.Current.FindResource("ModifiedBrush")
            };
        }
        return Application.Current.FindResource("ModifiedBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Subtracts a value from the input. Used for width calculations.
/// Parameter should be the amount to subtract.
/// </summary>
public class SubtractConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double doubleValue && parameter != null)
        {
            if (double.TryParse(parameter.ToString(), out double subtractValue))
            {
                return Math.Max(0, doubleValue - subtractValue);
            }
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts an integer to bool: true if greater than zero.
/// </summary>
public class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public class IntGreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && intValue > 0)
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public class TerminalRowHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return new GridLength(0);
        }

        var height = values[0] is double d ? d : 0;
        var isVisible = values[1] is bool b && b;
        return isVisible && height > 0 ? new GridLength(height) : new GridLength(0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        // OneWay converter - return array of DoNothing for each target type
        return targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }
}

/// <summary>
/// Formats a commit overflow count (e.g., "+ 1 commit", "+ 5 commits").
/// </summary>
public class OverflowCommitCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return $"+ {count} commit" + (count == 1 ? string.Empty : "s");
        }

        return "+ 0 commits";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts an identicon key string into an ImageSource.
/// </summary>
public class IdenticonConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value as string;
        int size = 16;
        if (parameter != null && int.TryParse(parameter.ToString(), out var parsed))
        {
            size = parsed;
        }

        var background = Utils.IdenticonGenerator.GetDefaultBackgroundColor();
        return Utils.IdenticonGenerator.GetIdenticon(key, size, background);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts a branch name to a GitFlow type indicator color.
/// Returns the appropriate brush for feature/release/hotfix branches, or Transparent for others.
/// </summary>
public class BranchNameToGitFlowColorConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush FeatureColor =
        new(System.Windows.Media.Color.FromRgb(0x82, 0x50, 0xDF)); // Purple
    private static readonly System.Windows.Media.SolidColorBrush ReleaseColor =
        new(System.Windows.Media.Color.FromRgb(0xBF, 0x87, 0x00)); // Yellow/Orange
    private static readonly System.Windows.Media.SolidColorBrush HotfixColor =
        new(System.Windows.Media.Color.FromRgb(0xCF, 0x22, 0x2E)); // Red
    private static readonly System.Windows.Media.SolidColorBrush SupportColor =
        new(System.Windows.Media.Color.FromRgb(0x57, 0x60, 0x6A)); // Gray
    private static readonly System.Windows.Media.SolidColorBrush MainColor =
        new(System.Windows.Media.Color.FromRgb(0x09, 0x69, 0xDA)); // Blue
    private static readonly System.Windows.Media.SolidColorBrush DevelopColor =
        new(System.Windows.Media.Color.FromRgb(0x1F, 0x88, 0x3D)); // Green

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string branchName)
            return System.Windows.Media.Brushes.Transparent;

        // Check for GitFlow prefixes
        if (branchName.StartsWith("feature/", StringComparison.OrdinalIgnoreCase))
            return FeatureColor;
        if (branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase))
            return ReleaseColor;
        if (branchName.StartsWith("hotfix/", StringComparison.OrdinalIgnoreCase))
            return HotfixColor;
        if (branchName.StartsWith("support/", StringComparison.OrdinalIgnoreCase))
            return SupportColor;
        if (branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("master", StringComparison.OrdinalIgnoreCase))
            return MainColor;
        if (branchName.Equals("develop", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("development", StringComparison.OrdinalIgnoreCase))
            return DevelopColor;

        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts a branch name to Visibility based on whether it's a GitFlow branch.
/// Returns Visible for feature/release/hotfix/support branches, Collapsed for others.
/// </summary>
public class BranchNameToGitFlowVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string branchName)
            return Visibility.Collapsed;

        // Check for GitFlow prefixes or main branches
        if (branchName.StartsWith("feature/", StringComparison.OrdinalIgnoreCase) ||
            branchName.StartsWith("release/", StringComparison.OrdinalIgnoreCase) ||
            branchName.StartsWith("hotfix/", StringComparison.OrdinalIgnoreCase) ||
            branchName.StartsWith("support/", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("master", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("develop", StringComparison.OrdinalIgnoreCase) ||
            branchName.Equals("development", StringComparison.OrdinalIgnoreCase))
        {
            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts a GitFlowBranchType to Visibility.
/// By default, returns Visible for any GitFlow branch type except None.
/// Pass "Finishable" as parameter to only show for Feature/Release/Hotfix/Support branches.
/// </summary>
public class GitFlowTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.GitFlowBranchType branchType)
        {
            // If "Finishable" parameter, only show for branches that can be finished
            if (parameter is string param && param.Equals("Finishable", StringComparison.OrdinalIgnoreCase))
            {
                return branchType is Models.GitFlowBranchType.Feature
                                  or Models.GitFlowBranchType.Release
                                  or Models.GitFlowBranchType.Hotfix
                                  or Models.GitFlowBranchType.Support
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // Default: show for all non-None types
            return branchType != Models.GitFlowBranchType.None ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts a filename to Visibility based on whether it's a Windows reserved filename.
/// Reserved names: CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9
/// Returns Visible for reserved names, Collapsed for normal files.
/// </summary>
public class ReservedFileNameToVisibilityConverter : IValueConverter
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string fileName)
            return Visibility.Collapsed;

        // Get filename without extension
        var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
        return ReservedNames.Contains(nameWithoutExt) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Returns true if the input string is null or empty.
/// Pass "Inverse" to invert the result.
/// </summary>
public class StringIsNullOrEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = value == null || (value is string str && string.IsNullOrEmpty(str));
        if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            return !isEmpty;
        }
        return isEmpty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts a SHA string to a stable brush from a fixed palette.
/// </summary>
public class ShaToBrushConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush[] Palette =
    [
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA7, 0x8B, 0xFA)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEC, 0x48, 0x99)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0xB8, 0xA6)),
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF9, 0x73, 0x16))
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string sha || string.IsNullOrWhiteSpace(sha))
        {
            return System.Windows.Media.Brushes.Transparent;
        }

        int hash = 0;
        foreach (var c in sha)
        {
            hash = (hash * 31) + c;
        }

        var index = Math.Abs(hash) % Palette.Length;
        return Palette[index];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Multiplies an integer (or double) by a double parameter.
/// </summary>
public class MultiplyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter == null || !double.TryParse(parameter.ToString(), out var factor))
        {
            return value;
        }

        if (value is int intValue)
        {
            return intValue * factor;
        }

        if (value is double doubleValue)
        {
            return doubleValue * factor;
        }

        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts blame line count and line height into a pixel height.
/// </summary>
public class BlameChunkHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
        {
            return 0d;
        }

        var count = values[0] is int i ? i : 0;
        var lineHeight = values[1] is double d ? d : 0d;
        return count * lineHeight;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        // OneWay converter - return array of DoNothing for each target type
        return targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }
}

/// <summary>
/// Converts a height to a CornerRadius that is half the height.
/// </summary>
public class HalfValueToCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            return new CornerRadius(height / 2.0);
        }

        return new CornerRadius(0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts an enum value to boolean for RadioButton binding.
/// Returns true if the value matches the converter parameter.
/// </summary>
public class EnumBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.Equals(parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return parameter;

        return Binding.DoNothing;
    }
}
