using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Leaf.Models;

namespace Leaf.Converters;

/// <summary>
/// Converts a <see cref="ContentMode"/> value to <see cref="Visibility"/>.
/// Returns Visible if the value matches the ConverterParameter, Collapsed otherwise.
/// Usage: ConverterParameter=Graph | PullRequestDetail | PullRequestCreate
/// </summary>
public class ContentModeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentMode mode && parameter is string param)
        {
            if (Enum.TryParse<ContentMode>(param, true, out var target))
            {
                return mode == target ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
