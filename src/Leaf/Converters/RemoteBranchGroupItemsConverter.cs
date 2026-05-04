using System.Globalization;
using System.Windows.Data;
using Leaf.Models;

namespace Leaf.Converters;

/// <summary>
/// Returns the merged tree-view children for a <see cref="RemoteBranchGroup"/>:
/// directory groups first (e.g. "feature", "hotfix"), then any ungrouped
/// branches whose remote-relative name has no "/" prefix. Mirrors
/// <see cref="BranchCategoryItemsConverter"/>'s LOCAL/GITFLOW merge so the
/// REMOTE section renders with the same folder layout (issue #29).
/// </summary>
public class RemoteBranchGroupItemsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not RemoteBranchGroup group)
        {
            return null;
        }

        if (group.DirectoryGroups.Count == 0)
        {
            return group.Branches;
        }

        var items = new List<object>(group.DirectoryGroups.Count + group.Branches.Count);
        items.AddRange(group.DirectoryGroups);
        items.AddRange(group.Branches);
        return items;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
