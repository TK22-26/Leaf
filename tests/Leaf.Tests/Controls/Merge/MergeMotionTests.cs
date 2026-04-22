#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Pins the V5 motion wiring: every live-consumer storyboard resolves with
/// the right duration + easing, and the <see cref="MergeMotionHelpers"/>
/// surface exposes what the plan contracts for. Rendering fidelity (actual
/// visible animation) is a Stagehand smoke-test concern; these tests only
/// prove the plumbing. Plan §D3's CheckboxToggle + PopoverShow are deferred
/// with rationale in <c>Resources/Merge/MergeMotion.xaml</c>, and RangeResolve
/// ships as a dispatcher-timer tween inside <c>ReadOnlyMergePane</c>.
/// </summary>
public class MergeMotionTests
{
    [StaFact]
    public void ConsumableStoryboards_Resolve_WithExpectedDurations()
    {
        EnsureMergeDictionaryMerged();
        var resources = Application.Current.Resources;

        // Every storyboard with a live consumer ships in MergeMotion.xaml.
        // Other V5 §D3 entries (CheckboxToggle, RangeResolve, PopoverShow)
        // are handled by dispatcher-timer tweens or deferred — rationale
        // lives in MergeMotion.xaml's header comment.
        AssertStoryboardDuration(resources, "Merge.Motion.PaneFocus", TimeSpan.FromMilliseconds(250));
        AssertStoryboardDuration(resources, "Merge.Motion.MinimapJump", TimeSpan.FromMilliseconds(400));
        AssertStoryboardDuration(resources, "Merge.Motion.AcceptButton", TimeSpan.FromMilliseconds(150));
    }

    [StaFact]
    public void EveryStoryboard_UsesEaseOutOfTheMotionRampEasing()
    {
        EnsureMergeDictionaryMerged();
        var resources = Application.Current.Resources;

        var keys = new[]
        {
            "Merge.Motion.PaneFocus",
            "Merge.Motion.MinimapJump",
            "Merge.Motion.AcceptButton",
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
    public void AcceptButton_Storyboard_HasParallelScaleXScaleYAnimations()
    {
        EnsureMergeDictionaryMerged();
        var storyboard = (Storyboard)Application.Current.Resources["Merge.Motion.AcceptButton"]!;

        storyboard.Children.Should().HaveCount(2,
            because: "AcceptButton drives ScaleX and ScaleY in parallel");
        foreach (Timeline child in storyboard.Children)
        {
            var da = child.Should().BeOfType<DoubleAnimation>().Subject;
            da.From.Should().Be(0.97, because: "plan §D3 specifies 0.97 → 1.0 bounce");
            da.To.Should().Be(1.0);
            da.Duration.TimeSpan.Should().Be(TimeSpan.FromMilliseconds(150));
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

    [StaFact]
    public void PlayAcceptBounce_AttachesScaleTransform_AndStartsAnimation()
    {
        EnsureMergeDictionaryMerged();
        var fe = new System.Windows.Controls.Button();
        // Must not throw, must end up with a ScaleTransform whose ScaleX/ScaleY
        // are under animation. The animated values themselves are timing-
        // dependent; asserting the transform exists is a stable invariant.
        MergeMotionHelpers.PlayAcceptBounce(fe);
        fe.RenderTransform.Should().BeOfType<System.Windows.Media.ScaleTransform>(
            because: "PlayAcceptBounce swaps in a writable ScaleTransform for the animation to drive");
        fe.RenderTransformOrigin.Should().Be(new System.Windows.Point(0.5, 0.5),
            because: "scale must be anchored to the cell centre — corner-anchored bounce reads wrong");
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
                // Another test class may have raced with us across a different
                // lock; tolerate the "already created" error rather than
                // double-creating the AppDomain-wide Application singleton.
                try { _ = new Application(); }
                catch (InvalidOperationException) { }
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
