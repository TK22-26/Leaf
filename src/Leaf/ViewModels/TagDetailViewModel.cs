using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// §5.17 — view-model for the tag detail pane that's swapped into the
/// right-hand area when the user selects a tag from the sidebar or
/// double-clicks a tag chip in the graph. Mirrors
/// <see cref="CommitDetailViewModel"/>'s shape so the two views feel
/// like siblings (header card → body → mini target → action buttons).
/// </summary>
public partial class TagDetailViewModel : ObservableObject
{
    /// <summary>
    /// Currently shown tag. Setting it null hides the right pane in
    /// the host XAML; setting non-null swaps the pane on.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTag))]
    [NotifyPropertyChangedFor(nameof(SignatureBadgeGlyph))]
    [NotifyPropertyChangedFor(nameof(SignatureBadgeBrush))]
    [NotifyPropertyChangedFor(nameof(SignatureFingerprintDisplay))]
    [NotifyPropertyChangedFor(nameof(KindLabel))]
    [NotifyPropertyChangedFor(nameof(TaggerDisplay))]
    [NotifyPropertyChangedFor(nameof(TaggerAvatarKey))]
    [NotifyPropertyChangedFor(nameof(TaggedAtDisplay))]
    [NotifyPropertyChangedFor(nameof(MessageBody))]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(TargetCommit))]
    private TagInfo? _tag;

    /// <summary>
    /// The commit that the tag points to — populated by the host VM
    /// after Tag is set so the detail can show a mini-card and let the
    /// user navigate to the source commit.
    /// </summary>
    [ObservableProperty]
    private CommitInfo? _targetCommit;

    public bool HasTag => Tag is not null;
    public bool HasMessage => !string.IsNullOrWhiteSpace(MessageBody);

    /// <summary>One-word badge: signed / annotated / lightweight.</summary>
    public string KindLabel => Tag is null
        ? string.Empty
        : Tag.IsSigned ? "Signed annotated tag"
        : Tag.IsAnnotated ? "Annotated tag"
        : "Lightweight tag";

    /// <summary>"Name &lt;email&gt;" or just name when email is missing.</summary>
    public string TaggerDisplay
    {
        get
        {
            if (Tag is null) return string.Empty;
            var name = Tag.TaggerName ?? string.Empty;
            var email = Tag.TaggerEmail ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email)) return string.Empty;
            if (string.IsNullOrWhiteSpace(email)) return name;
            return string.IsNullOrWhiteSpace(name) ? email : $"{name} <{email}>";
        }
    }

    /// <summary>Identicon key — same heuristic as CommitInfo.AvatarKey.</summary>
    public string TaggerAvatarKey => Tag?.TaggerEmail is { Length: > 0 } e
        ? e.Trim()
        : (Tag?.TaggerName ?? string.Empty).Trim();

    public string TaggedAtDisplay => Tag?.TaggedAt is { } t
        ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    /// <summary>
    /// Annotation message stripped of the trailing PGP signature block
    /// when present — git stores the inline signature inside the tag's
    /// raw object, but the user only wants to read the message above it.
    /// </summary>
    public string MessageBody
    {
        get
        {
            var raw = Tag?.Message;
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var idx = raw.IndexOf("-----BEGIN PGP SIGNATURE-----", StringComparison.Ordinal);
            if (idx < 0) idx = raw.IndexOf("-----BEGIN SSH SIGNATURE-----", StringComparison.Ordinal);
            return idx > 0 ? raw[..idx].TrimEnd() : raw.TrimEnd();
        }
    }

    /// <summary>Segoe Fluent Icons glyph for the signature badge — mirrors CommitDetailViewModel.SignatureBadgeGlyph.</summary>
    public string SignatureBadgeGlyph => Tag?.SignatureStatus switch
    {
        CommitSignatureStatus.Valid => "",
        CommitSignatureStatus.UnknownKey => "",
        CommitSignatureStatus.UntrustedKey => "",
        CommitSignatureStatus.Expired => "",
        CommitSignatureStatus.ExpiredKey => "",
        CommitSignatureStatus.RevokedKey => "",
        CommitSignatureStatus.Bad => "",
        _ => string.Empty,
    };

    /// <summary>Brush for the signature glyph — same palette as CommitDetailViewModel.</summary>
    public Brush SignatureBadgeBrush => Tag?.SignatureStatus switch
    {
        CommitSignatureStatus.Valid => SignatureBrushes.Green,
        CommitSignatureStatus.UnknownKey => SignatureBrushes.Amber,
        CommitSignatureStatus.UntrustedKey => SignatureBrushes.Amber,
        CommitSignatureStatus.Expired => SignatureBrushes.Amber,
        CommitSignatureStatus.ExpiredKey => SignatureBrushes.Amber,
        CommitSignatureStatus.RevokedKey => SignatureBrushes.Red,
        CommitSignatureStatus.Bad => SignatureBrushes.Red,
        _ => SignatureBrushes.Neutral,
    };

    /// <summary>Multi-line tooltip for the signature row — summary + signer + fingerprint.</summary>
    public string SignatureFingerprintDisplay
    {
        get
        {
            if (Tag is null || !Tag.IsSigned) return string.Empty;
            var lines = new List<string>(3) { Tag.SignatureSummary };
            if (!string.IsNullOrWhiteSpace(Tag.SignerEmail))
                lines.Add(string.IsNullOrWhiteSpace(Tag.SignerName)
                    ? Tag.SignerEmail
                    : $"{Tag.SignerName} <{Tag.SignerEmail}>");
            if (!string.IsNullOrWhiteSpace(Tag.SignerKeyFingerprint))
                lines.Add($"Key: {Tag.SignerKeyFingerprint}");
            return string.Join('\n', lines);
        }
    }

    /// <summary>
    /// Forwards the host VM's tag commands so the detail view's action
    /// buttons can drive Checkout / Push / Delete without binding
    /// through the visual tree.
    /// </summary>
    public IRelayCommand<TagInfo>? CheckoutTagCommand { get; set; }
    public IAsyncRelayCommand<TagInfo>? PushTagCommand { get; set; }
    public IAsyncRelayCommand<TagInfo>? DeleteTagCommand { get; set; }

    /// <summary>Navigate the graph to the tagged commit (Tag.TargetSha). Set by the host VM.</summary>
    public Action<string>? NavigateToCommit { get; set; }

    [RelayCommand]
    private void NavigateToTargetCommit()
    {
        if (Tag is null) return;
        NavigateToCommit?.Invoke(Tag.TargetSha);
    }

    private static class SignatureBrushes
    {
        public static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43)));
        public static readonly Brush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xBF, 0x83, 0x00)));
        public static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0x35, 0x35)));
        public static readonly Brush Neutral = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
