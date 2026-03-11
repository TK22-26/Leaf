using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Leaf.Helpers;

/// <summary>
/// Extension methods for WPF visual tree traversal.
/// </summary>
internal static class VisualTreeExtensions
{
    /// <summary>
    /// Recursively searches the visual tree for a ScrollViewer descendant.
    /// </summary>
    public static ScrollViewer? FindScrollViewer(DependencyObject? root)
    {
        if (root == null) return null;
        if (root is ScrollViewer sv) return sv;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }
}
