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
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }
}
