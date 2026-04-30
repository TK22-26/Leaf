namespace Leaf.Models;

/// <summary>
/// A single entry in the apply-patch preview pane. Reflects what the
/// user will get if they confirm: one new commit (with this author /
/// date / subject) per <c>.patch</c> file when applied via
/// <see cref="ApplyPatchStrategy.Am"/>.
/// </summary>
/// <param name="FilePath">Absolute path to the source <c>.patch</c> file on disk.</param>
/// <param name="Subject">Commit subject parsed out of the <c>Subject:</c> mail header.</param>
/// <param name="Author">Author identity (<c>"Name &lt;email&gt;"</c>) from the <c>From:</c> header.</param>
/// <param name="AuthoredWhen">Authored timestamp from the <c>Date:</c> header.</param>
/// <param name="HasParseError">True when the file didn't look like a valid format-patch output. <see cref="Subject"/> falls back to the file name.</param>
public sealed record PatchPreviewItem(
    string FilePath,
    string Subject,
    string Author,
    DateTimeOffset AuthoredWhen,
    bool HasParseError);
