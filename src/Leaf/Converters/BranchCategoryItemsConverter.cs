using System.Collections;
using System.Globalization;
using System.Windows.Data;
using Leaf.Models;

namespace Leaf.Converters;

/// <summary>
/// Returns the appropriate items collection for a BranchCategory (Branches for LOCAL, RemoteGroups for REMOTE).
/// </summary>
public class BranchCategoryItemsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BranchCategory category)
        {
            return category.IsRemoteCategory ? category.RemoteGroups : category.Branches;
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
