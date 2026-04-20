#nullable enable
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Leaf.Controls.Merge;

/// <summary>
/// Attached property that installs the merge editor's standard drop shadow
/// (8 px blur, symmetric) on any <see cref="UIElement"/>. The shadow colour
/// and opacity come from the palette tokens
/// <c>Merge.Shadow.Color</c> + <c>Merge.Shadow.Opacity</c>, so V8's theme
/// swap propagates to the shadow automatically without any consumer change.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from an inline <c>&lt;Setter Property="Effect"&gt;</c> in
/// <c>MergeCardStyles.xaml</c> so the effect's colour is themeable rather
/// than the hard-coded <c>#FF000000</c> that
/// <see cref="DropShadowEffect.Color"/> defaults to. Consumers opt in by
/// setting <c>merge:ThemeShadow.IsEnabled="True"</c> on their element.
/// </para>
/// </remarks>
public static class ThemeShadow
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ThemeShadow),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.Effect = (bool)e.NewValue ? BuildEffect() : null;
    }

    private static DropShadowEffect BuildEffect()
    {
        return new DropShadowEffect
        {
            BlurRadius = 8,
            ShadowDepth = 0,
            Color = MergePaletteResources.ResolveColor("Merge.Shadow.Color"),
            Opacity = MergePaletteResources.Resolve<double>("Merge.Shadow.Opacity"),
        };
    }
}
