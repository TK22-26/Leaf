#nullable enable
using System.Windows;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Shared one-time palette load for every merge test that needs
/// <c>Application.Current.Resources</c> populated with the
/// <c>Merge.xaml</c> umbrella (motion storyboards, palette colours,
/// typography). Replaces the EnsureMergeDictionaryMerged helper that
/// was previously copy-pasted across MergeMotionTests,
/// BlamePeekPopoverTests, BlameHoverControllerTests, and the
/// SegmentedAcceptPill fixture per the "NEVER duplicate code" policy.
/// </summary>
/// <remarks>
/// Idempotent and thread-safe: first touch creates the
/// <see cref="Application"/> singleton (if missing) and merges the
/// dictionary; subsequent calls are no-ops. Tolerates a sibling test
/// class having already called <c>new Application()</c>. Use a
/// static call — not a class-fixture instance — because xunit's
/// parallel test-class runner would otherwise need every class to
/// declare the fixture, and the Application state is AppDomain-wide
/// anyway so class-scoped isolation is fictional.
/// </remarks>
internal static class MergePaletteTestFixture
{
    private static readonly object _lock = new();
    private static bool _merged;

    public static void Ensure()
    {
        lock (_lock)
        {
            if (Application.Current is null)
            {
                try { _ = new Application(); }
                catch (InvalidOperationException) { /* another test raced in, ok */ }
            }
            if (_merged) return;

            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Leaf;component/Resources/Merge/Merge.xaml",
                    UriKind.Absolute),
            };
            Application.Current!.Resources.MergedDictionaries.Add(dict);
            _merged = true;
        }
    }
}
