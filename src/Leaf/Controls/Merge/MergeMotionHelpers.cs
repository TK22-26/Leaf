#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Leaf.Controls.Merge;

/// <summary>
/// Merge editor motion wiring. Three helpers ship today —
/// <see cref="SmoothScrollTo"/> drives <c>Merge.Motion.MinimapJump</c> on a
/// <see cref="ScrollViewer"/>, <see cref="PulsePaneFocusColour"/> drives
/// <c>Merge.Motion.PaneFocus</c> on a <see cref="Border"/>'s
/// <see cref="Border.BorderBrush"/>, and <see cref="PlayAcceptBounce"/>
/// drives <c>Merge.Motion.AcceptButton</c> on a clicked
/// <see cref="FrameworkElement"/>. The range-resolve animation lives as a
/// dispatcher-timer-driven pure-math tween inside
/// <see cref="ReadOnlyMergePane"/> because Storyboards can't drive surfaces
/// rendered directly via <c>DrawingContext</c>. CheckboxToggle + PopoverShow
/// from V5's original spec are deferred — rationale in MergeMotion.xaml.
/// </summary>
/// <remarks>
/// Every motion helper resolves its Storyboard through
/// <see cref="MergePaletteResources.Resolve{T}"/>, which throws on a missing
/// key rather than silently no-op'ing. A deleted or mistyped
/// <c>Merge.Motion.*</c> resource is a programming error; falling through
/// quietly would mask the regression.
/// </remarks>
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
        var storyboard = CloneStoryboard("Merge.Motion.MinimapJump");
        var anim = AssertSingleAnimation<DoubleAnimation>(storyboard, "Merge.Motion.MinimapJump");
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
        var storyboard = CloneStoryboard("Merge.Motion.PaneFocus");
        var anim = AssertSingleAnimation<ColorAnimation>(storyboard, "Merge.Motion.PaneFocus");

        // PaneCard.BorderBrush is always a SolidColorBrush from
        // Merge.Border.Subtle (see MergeCardStyles.xaml) — gradient / null /
        // ImageBrush would indicate the pane was re-styled away from PaneCard,
        // at which point the pulse animation's ColorAnimation has no valid
        // From/To to target. Fail loudly rather than silently substituting
        // targetColor and producing a zero-animation pulse.
        if (border.BorderBrush is not SolidColorBrush solidBrush)
        {
            throw new InvalidOperationException(
                "PulsePaneFocusColour requires a SolidColorBrush BorderBrush (the Merge.PaneCard " +
                "style supplies one from Merge.Border.Subtle). Received: " +
                (border.BorderBrush?.GetType().Name ?? "null") + ".");
        }
        var currentColor = solidBrush.Color;

        // Replace the BorderBrush with a per-instance unfrozen SolidColorBrush
        // so ColorAnimation has something writable to drive. Palette brushes
        // come through DynamicResource as Frozen, which would refuse the write.
        var animated = new SolidColorBrush(currentColor);
        border.BorderBrush = animated;

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

    /// <summary>
    /// Play the <c>Merge.Motion.AcceptButton</c> scale-bounce on
    /// <paramref name="target"/> — plan §D3's 150 ms 0.97 → 1.0 click feedback.
    /// Attaches a <see cref="ScaleTransform"/> as the element's RenderTransform
    /// with centred origin so the bounce scales around the cell's midpoint.
    /// </summary>
    public static void PlayAcceptBounce(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var storyboard = CloneStoryboard("Merge.Motion.AcceptButton");
        if (storyboard.Children.Count < 2)
        {
            throw new InvalidOperationException(
                "Merge.Motion.AcceptButton must have two DoubleAnimation children " +
                "(ScaleX + ScaleY). Check Resources/Merge/MergeMotion.xaml.");
        }

        // Ensure a writable ScaleTransform is in place — existing RenderTransform
        // (even a frozen identity) would refuse the animated writes. Centre the
        // origin so the bounce feels anchored to the cell rather than the corner.
        var scale = new ScaleTransform(1.0, 1.0);
        target.RenderTransform = scale;
        target.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleX = (DoubleAnimation)storyboard.Children[0];
        Storyboard.SetTarget(scaleX, scale);
        Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));

        var scaleY = (DoubleAnimation)storyboard.Children[1];
        Storyboard.SetTarget(scaleY, scale);
        Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));

        storyboard.Begin();
    }

    private static Storyboard CloneStoryboard(string resourceKey)
    {
        // Strict resolve — throws on missing key. A silent fallback would
        // mask a deleted or renamed palette token and ship a feature that
        // looks like it's working (no exception) but has no motion.
        var source = MergePaletteResources.Resolve<Storyboard>(resourceKey);
        // Clone the shared resource so customising TargetProperty / From / To
        // doesn't race with concurrent consumers (the storyboard is a shared
        // Freezable).
        return source.Clone();
    }

    private static TAnimation AssertSingleAnimation<TAnimation>(Storyboard storyboard, string resourceKey)
        where TAnimation : Timeline
    {
        if (storyboard.Children.Count == 0 || storyboard.Children[0] is not TAnimation anim)
        {
            throw new InvalidOperationException(
                $"'{resourceKey}' must contain a single {typeof(TAnimation).Name} as its first child. " +
                "Check Resources/Merge/MergeMotion.xaml.");
        }
        return anim;
    }
}
