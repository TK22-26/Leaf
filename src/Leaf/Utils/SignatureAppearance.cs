using System.Windows.Media;
using Leaf.Models;

namespace Leaf.Utils;

/// <summary>
/// Single source of truth for the visual appearance of a signature
/// status (glyph + colour) across the graph badge, the commit-detail
/// header, and the tag-detail header. Without this, three call sites
/// each had their own switch — a tweak to amber or to a glyph would
/// silently desync the surfaces.
/// </summary>
internal static class SignatureAppearance
{
    /// <summary>
    /// Segoe Fluent Icons glyph PUA character for the signature status,
    /// or empty string for <see cref="CommitSignatureStatus.None"/> /
    /// unknown values. Glyphs are picked to mirror the semantics — a
    /// check for verified, a warning chevron for trust issues, an X
    /// for revoked or bad.
    /// </summary>
    public static string GlyphFor(CommitSignatureStatus status) => status switch
    {
        CommitSignatureStatus.Valid        => "", // CheckMark
        CommitSignatureStatus.UnknownKey   => "", // Info
        CommitSignatureStatus.UntrustedKey => "", // Warning
        CommitSignatureStatus.Expired      => "", // Recent (clock)
        CommitSignatureStatus.ExpiredKey   => "",
        CommitSignatureStatus.RevokedKey   => "", // Cancel
        CommitSignatureStatus.Bad          => "", // ErrorBadge
        _ => string.Empty,
    };

    /// <summary>Solid colour matching the glyph's semantics.</summary>
    public static Color ColorFor(CommitSignatureStatus status) => status switch
    {
        CommitSignatureStatus.Valid        => GreenColor,
        CommitSignatureStatus.UnknownKey   => AmberColor,
        CommitSignatureStatus.UntrustedKey => AmberColor,
        CommitSignatureStatus.Expired      => AmberColor,
        CommitSignatureStatus.ExpiredKey   => AmberColor,
        CommitSignatureStatus.RevokedKey   => RedColor,
        CommitSignatureStatus.Bad          => RedColor,
        _ => NeutralColor,
    };

    /// <summary>Frozen brush, safe to share across visuals on any thread.</summary>
    public static Brush BrushFor(CommitSignatureStatus status) => status switch
    {
        CommitSignatureStatus.Valid        => GreenBrush,
        CommitSignatureStatus.UnknownKey   => AmberBrush,
        CommitSignatureStatus.UntrustedKey => AmberBrush,
        CommitSignatureStatus.Expired      => AmberBrush,
        CommitSignatureStatus.ExpiredKey   => AmberBrush,
        CommitSignatureStatus.RevokedKey   => RedBrush,
        CommitSignatureStatus.Bad          => RedBrush,
        _ => NeutralBrush,
    };

    private static readonly Color GreenColor = Color.FromRgb(0x2E, 0xA0, 0x43);
    private static readonly Color AmberColor = Color.FromRgb(0xBF, 0x83, 0x00);
    private static readonly Color RedColor = Color.FromRgb(0xC8, 0x35, 0x35);
    private static readonly Color NeutralColor = Color.FromRgb(0x80, 0x80, 0x80);

    private static readonly Brush GreenBrush = Frozen(GreenColor);
    private static readonly Brush AmberBrush = Frozen(AmberColor);
    private static readonly Brush RedBrush = Frozen(RedColor);
    private static readonly Brush NeutralBrush = Frozen(NeutralColor);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
