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
            if (category.IsTagsCategory) return category.Tags;
            if (category.IsWorktreesCategory) return category.Worktrees;
            if (category.IsPullRequestsCategory) return category.PullRequests;
            if (category.IsSubmodulesCategory) return category.Submodules;
            if (category.IsRemoteCategory) return category.RemoteGroups;

            // LOCAL and GITFLOW: merge directory groups with ungrouped branches
            if (category.DirectoryGroups.Count > 0)
            {
                var items = new List<object>();
                items.AddRange(category.DirectoryGroups);
                items.AddRange(category.Branches);
                return items;
            }

            return category.Branches;
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // OneWay converter - ConvertBack not supported
        return Binding.DoNothing;
    }
}
