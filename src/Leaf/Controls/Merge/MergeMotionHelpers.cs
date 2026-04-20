#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Leaf.Controls.Merge;

/// <summary>
/// Merge editor motion wiring. Today the only helper live in production is
/// <see cref="SmoothScrollTo"/> — it's the only one of V5's six storyboard
/// cases whose target (a <see cref="ScrollViewer"/>) is a real WPF element
/// with a DP that's sensible to animate via the Storyboard path. The other
/// five storyboards (CheckboxToggle / AcceptButton / RangeResolve /
/// PaneFocus / PopoverShow) target surfaces that either (a) are drawn
/// directly by <see cref="ReadOnlyMergePane.OnRender"/> and so animate
/// through dispatcher-timer-driven pure-math tweens instead of Storyboards,
/// or (b) don't have consumers yet (popovers land in C5 / AI proposal UI).
/// When those consumers arrive, they add their own helpers here rather than
/// inheriting dormant scaffolding that would be indistinguishable from
/// already-wired code.
/// </summary>
internal static class MergeMotionHelpers
{
    /// <summary>
    /// Animated scroll offset attached property on <see cref="ScrollViewer"/>.
    /// Writes flow through to <see cref="ScrollViewer.ScrollToVerticalOffset"/>
    /// so animating this DP produces a smooth scroll — the native
    /// <see cref="ScrollViewer.VerticalOffset"/> is read-only and can't be
    /// animated directly.
    /// </summary>
    public static readonly DependencyProperty AnimatedVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedVerticalOffset",
            typeof(double),
            typeof(MergeMotionHelpers),
            new PropertyMetadata(0.0, OnAnimatedVerticalOffsetChanged));

    public static void SetAnimatedVerticalOffset(DependencyObject d, double value) =>
        d.SetValue(AnimatedVerticalOffsetProperty, value);

    public static double GetAnimatedVerticalOffset(DependencyObject d) =>
        (double)d.GetValue(AnimatedVerticalOffsetProperty);

    private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    /// <summary>
    /// Smoothly scroll <paramref name="sv"/> to <paramref name="targetOffset"/>
    /// using <c>Merge.Motion.MinimapJump</c>'s duration + easing.
    /// </summary>
    public static void SmoothScrollTo(ScrollViewer sv, double targetOffset)
    {
        ArgumentNullException.ThrowIfNull(sv);
        if (Application.Current is not { } app) return;
        if (app.TryFindResource("Merge.Motion.MinimapJump") is not Storyboard source) return;

        // Clone the shared resource so customising its TargetProperty / From /
        // To doesn't race with concurrent consumers (the storyboard is a shared
        // Freezable).
        var storyboard = source.Clone();
        if (storyboard.Children.Count == 0) return;

        var anim = (DoubleAnimation)storyboard.Children[0];
        anim.From = sv.VerticalOffset;
        anim.To = targetOffset;
        Storyboard.SetTarget(anim, sv);
        Storyboard.SetTargetProperty(anim, new PropertyPath(AnimatedVerticalOffsetProperty));
        storyboard.Begin();
    }

    /// <summary>
    /// Pulse <paramref name="border"/>'s <see cref="Border.BorderBrush"/> colour
    /// toward <paramref name="targetColor"/> over <c>Merge.Motion.PaneFocus</c>
    /// (250 ms ease-out). When <paramref name="restoreResourceKey"/> is non-null
    /// the animation's Completed hook rebinds the BorderBrush to that
    /// <see cref="DynamicResourceExtension"/> key so the palette stays the
    /// single source of truth when focus leaves (V8 theme swap keeps working).
    /// </summary>
    public static void PulsePaneFocusColour(Border border, Color targetColor, string? restoreResourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(border);
        if (Application.Current is not { } app) return;
        if (app.TryFindResource("Merge.Motion.PaneFocus") is not Storyboard source) return;

        // Replace the BorderBrush with a per-instance unfrozen SolidColorBrush
        // so ColorAnimation has something writable to drive. Palette brushes
        // come through DynamicResource as Frozen, which would refuse the write.
        var currentColor = (border.BorderBrush as SolidColorBrush)?.Color ?? targetColor;
        var animated = new SolidColorBrush(currentColor);
        border.BorderBrush = animated;

        var storyboard = source.Clone();
        if (storyboard.Children.Count == 0) return;
        var anim = (ColorAnimation)storyboard.Children[0];
        anim.From = currentColor;
        anim.To = targetColor;
        Storyboard.SetTarget(anim, animated);
        Storyboard.SetTargetProperty(anim, new PropertyPath("Color"));

        if (restoreResourceKey is not null)
        {
            storyboard.Completed += (_, _) =>
                border.SetResourceReference(Border.BorderBrushProperty, restoreResourceKey);
        }

        storyboard.Begin();
    }
}
