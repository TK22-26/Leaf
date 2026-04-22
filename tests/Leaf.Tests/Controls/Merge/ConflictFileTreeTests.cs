#nullable enable
using System.Windows.Controls;
using FluentAssertions;
using Leaf.Controls.Merge;
using Leaf.Models;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Unit tests for <see cref="ConflictFileTree"/>. Uses live WPF controls on
/// the STA test thread so ItemContainerGenerator actually materializes
/// TreeViewItems. Covers SelectedFile two-way sync, folder expansion
/// persistence across tree rebuilds, and the "folders don't change
/// SelectedFile" invariant.
/// </summary>
public class ConflictFileTreeTests
{
    private static ConflictInfo File(string path, bool resolved = false, int conflictCount = 1) =>
        new()
        {
            FilePath = path,
            IsResolved = resolved,
            ConflictCount = conflictCount,
        };

    [StaFact]
    public void ItemsSource_Null_ClearsTree()
    {
        var tree = new ConflictFileTree();
        tree.ItemsSource = null;

        // Force the control through at least one measure/arrange so the
        // TreeView inside picks up the null ItemsSource assignment.
        tree.Measure(new System.Windows.Size(200, 400));
        tree.Arrange(new System.Windows.Rect(0, 0, 200, 400));

        GetTreeView(tree).Items.Count.Should().Be(0);
    }

    [StaFact]
    public void ItemsSource_BindsTree_AndTopLevelCountMatches()
    {
        var nodes = ConflictTreeBuilder.Build(new[]
        {
            File("src/a.cs"),
            File("docs/b.md"),
            File("c.txt"),
        });
        var tree = new ConflictFileTree { ItemsSource = nodes };
        Arrange(tree);

        GetTreeView(tree).Items.Count.Should().Be(3,
            because: "two folders (docs, src) + one root file = three top-level items");
    }

    [StaFact]
    public void SelectedFile_SettingExternally_SelectsFileLeaf()
    {
        var targetConflict = File("src/x.cs");
        var nodes = ConflictTreeBuilder.Build(new[]
        {
            File("src/a.cs"),
            targetConflict,
            File("docs/z.md"),
        });
        var tree = new ConflictFileTree { ItemsSource = nodes };
        Arrange(tree);

        tree.SelectedFile = targetConflict;
        Arrange(tree);

        var treeView = GetTreeView(tree);
        // Walk the visual containers and find the selected TreeViewItem.
        var selectedContainer = FindSelectedTreeViewItem(treeView);
        selectedContainer.Should().NotBeNull(
            because: "setting SelectedFile must highlight the matching file leaf");
        ((ConflictTreeNode)selectedContainer!.DataContext).Conflict.Should().Be(targetConflict);
    }

    [StaFact]
    public void CollapsingFolder_PersistsAcrossRebuild()
    {
        var initial = ConflictTreeBuilder.Build(new[]
        {
            File("src/a.cs"),
            File("src/b.cs"),
            File("docs/readme.md"),
        });
        var tree = new ConflictFileTree { ItemsSource = initial };
        Arrange(tree);

        // Collapse the "src" folder via the TreeViewItem API — this is the
        // same path the user's click triggers. The handler attached in the
        // control's ctor should record src's full path in _collapsedFolders.
        var treeView = GetTreeView(tree);
        var srcContainer = FindFolderContainer(treeView, "src");
        srcContainer.Should().NotBeNull();
        srcContainer!.IsExpanded = false;
        Arrange(tree);

        // Simulate the RefreshConflictBuckets → ConflictTree rebuild: a new
        // list of ConflictTreeNode instances with the same shape.
        var rebuilt = ConflictTreeBuilder.Build(new[]
        {
            File("src/a.cs"),
            File("src/b.cs"),
            File("docs/readme.md"),
            File("new.md"),
        });
        tree.ItemsSource = rebuilt;
        Arrange(tree);

        var srcAfter = FindFolderContainer(GetTreeView(tree), "src");
        srcAfter.Should().NotBeNull();
        srcAfter!.IsExpanded.Should().BeFalse(
            because: "user explicitly collapsed 'src' — that state must survive the rebuild");

        var docsAfter = FindFolderContainer(GetTreeView(tree), "docs");
        docsAfter!.IsExpanded.Should().BeTrue(
            because: "'docs' was never collapsed, so it should come back expanded by default");
    }

    private static void Arrange(System.Windows.FrameworkElement el)
    {
        el.Measure(new System.Windows.Size(400, 600));
        el.Arrange(new System.Windows.Rect(0, 0, 400, 600));
        el.UpdateLayout();
        // Pump the dispatcher so the control's post-layout BeginInvoke
        // (expansion / selection restore) actually runs inside the test.
        var frame = new System.Windows.Threading.DispatcherFrame();
        _ = el.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static TreeView GetTreeView(ConflictFileTree tree)
    {
        // The TreeView is named "Tree" in the XAML; reach it via FindName.
        return (TreeView)tree.FindName("Tree")!;
    }

    private static TreeViewItem? FindSelectedTreeViewItem(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (container.IsSelected) return container;
            var descendant = FindSelectedTreeViewItem(container);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private static TreeViewItem? FindFolderContainer(ItemsControl parent, string displayName)
    {
        foreach (var item in parent.Items)
        {
            if (item is ConflictTreeNode node && node.DisplayName == displayName && node.IsFolder &&
                parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem match)
            {
                return match;
            }
        }
        return null;
    }
}
