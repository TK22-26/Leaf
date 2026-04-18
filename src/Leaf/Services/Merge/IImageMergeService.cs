#nullable enable
namespace Leaf.Services.Merge;

/// <summary>
/// Loads and classifies image payloads for the Phase 6 image conflict pane.
/// Keeps all byte-level work (git-show shell-outs, magic-byte sniffing,
/// LFS-pointer detection) out of the ViewModel — the VM only sees the
/// structured <see cref="ImageConflictPayload"/>.
/// </summary>
public interface IImageMergeService
{
    /// <summary>
    /// Load ours/theirs/base bytes for <paramref name="filePath"/> (repo-relative).
    /// Sides that don't exist in their stage (e.g. ours-added vs theirs-added)
    /// come back with null bytes, not thrown exceptions — the UI still needs
    /// to render the other side.
    /// </summary>
    ImageConflictPayload Load(string repoPath, string filePath);
}

/// <summary>
/// Structured per-side payload. Bytes are <c>null</c> for a missing stage or
/// a non-binary side; format classifies what the bitmap decoder can do with
/// the bytes.
/// </summary>
public sealed record ImageConflictPayload(
    string FilePath,
    ImageSidePayload Ours,
    ImageSidePayload Theirs,
    ImageSidePayload Base);

public sealed record ImageSidePayload(
    byte[]? Bytes,
    ImageFormat Format,
    bool IsLfsPointer);

public enum ImageFormat
{
    /// <summary>No payload at all (missing stage).</summary>
    None,
    Png,
    Jpeg,
    Gif,
    Bmp,
    Webp,
    /// <summary>
    /// Payload exists but the magic bytes don't match a recognised format.
    /// Rendered as "binary — use ours / use theirs" without a preview.
    /// </summary>
    Unknown,
}
