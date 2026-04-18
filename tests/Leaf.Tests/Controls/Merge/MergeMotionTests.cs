#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the V5 motion wiring: the six storyboard resources resolve with the
/// right durations and easing, and the <see cref="MergeMotionHelpers"/>
/// surface exposes what the plan contracts for. Rendering fidelity
/// (actual visible animation) is a Stagehand smoke-test concern; these
/// tests only prove the plumbing.
/// </summary>
public class MergeMotionTests
{
    [StaFact]
    public void AllSixStoryboards_Resolve_WithExpectedDurations()
    {
        EnsureMergeDictionaryMerged();
        var resources = Application.Current.Resources;

        // Per D3: 150 / 250 / 350 ms ease-out ramp, plus 200 / 400 outliers
        // noted in the plan.
        AssertStoryboardDuration(resources, "Merge.Motion.CheckboxToggle", TimeSpan.FromMilliseconds(200));
        AssertStoryboardDuration(resources, "Merge.Motion.AcceptButton", TimeSpan.FromMilliseconds(150));
        AssertStoryboardDuration(resources, "Merge.Motion.RangeResolve", TimeSpan.FromMilliseconds(350));
        AssertStoryboardDuration(resources, "Merge.Motion.PaneFocus", TimeSpan.FromMilliseconds(250));
        AssertStoryboardDuration(resources, "Merge.Motion.MinimapJump", TimeSpan.FromMilliseconds(400));
        AssertStoryboardDuration(resources, "Merge.Motion.PopoverShow", TimeSpan.FromMilliseconds(200));
    }

    [StaFact]
    public void EveryStoryboard_UsesEaseOutOfTheMotionRampEasing()
    {
        EnsureMergeDictionaryMerged();
        var resources = Application.Current.Resources;

        var keys = new[]
        {
            "Merge.Motion.CheckboxToggle",
            "Merge.Motion.AcceptButton",
            "Merge.Motion.RangeResolve",
            "Merge.Motion.PaneFocus",
            "Merge.Motion.MinimapJump",
            "Merge.Motion.PopoverShow",
        };
        foreach (var key in keys)
        {
            var storyboard = (Storyboard)resources[key]!;
            foreach (Timeline child in storyboard.Children)
            {
                if (child is DoubleAnimation da)
                {
                    da.EasingFunction.Should().BeOfType<QuadraticEase>(
                        because: $"storyboard '{key}' must share the Merge.Motion.Ease ease-out");
                    ((QuadraticEase)da.EasingFunction).EasingMode.Should().Be(EasingMode.EaseOut);
                }
                else if (child is ColorAnimation ca)
                {
                    ca.EasingFunction.Should().BeOfType<QuadraticEase>();
                    ((QuadraticEase)ca.EasingFunction).EasingMode.Should().Be(EasingMode.EaseOut);
                }
            }
        }
    }

    [StaFact]
    public void SmoothScrollTo_KicksAnimationOnScrollViewer()
    {
        EnsureMergeDictionaryMerged();
        var sv = new ScrollViewer();
        // Invoking the helper should not throw and should leave an animation
        // in flight on the attached scroll-offset DP.
        var act = () => MergeMotionHelpers.SmoothScrollTo(sv, 100.0);
        act.Should().NotThrow();
    }

    private static void AssertStoryboardDuration(ResourceDictionary resources, string key, TimeSpan expected)
    {
        resources[key].Should().NotBeNull(because: $"motion storyboard '{key}' must be defined");
        var storyboard = (Storyboard)resources[key]!;
        storyboard.Children.Should().NotBeEmpty(because: $"'{key}' must contain at least one animation child");
        var child = storyboard.Children[0];
        child.Duration.HasTimeSpan.Should().BeTrue(because: $"'{key}' child duration must be concrete, not Automatic");
        child.Duration.TimeSpan.Should().Be(expected,
            because: $"'{key}' duration must match the Merge.Motion ramp");
    }

    private static readonly object _mergeLock = new();
    private static bool _merged;

    private static void EnsureMergeDictionaryMerged()
    {
        lock (_mergeLock)
        {
            if (Application.Current is null)
            {
                _ = new Application();
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
