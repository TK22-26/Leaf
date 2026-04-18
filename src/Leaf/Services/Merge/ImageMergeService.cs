#nullable enable
using System.Text;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Merge;

/// <summary>
/// Loads image bytes from conflict stages via <c>git show</c> and classifies
/// them by magic bytes. LFS pointer detection is a tiny-payload heuristic:
/// the raw pointer file is ≤ 200 bytes of ASCII starting with
/// <c>version https://git-lfs.github.com/spec/v1</c>, so we don't even need
/// the LFS binary to know a smudge is needed.
/// </summary>
public sealed class ImageMergeService : IImageMergeService
{
    // Well-known LFS-pointer prefix. Smaller than the actual file but
    // distinct enough that a collision with a real image is essentially zero.
    private static readonly byte[] LfsPointerPrefix =
        Encoding.ASCII.GetBytes("version https://git-lfs.github.com/spec/v1");

    public ImageConflictPayload Load(string repoPath, string filePath)
    {
        ArgumentNullException.ThrowIfNull(repoPath);
        ArgumentNullException.ThrowIfNull(filePath);

        // Stages: 1 = base, 2 = ours (HEAD), 3 = theirs (MERGE_HEAD).
        // During a merge the index holds all three for conflicting entries.
        var baseBytes = GitCliHelpers.ReadConflictStageBytes(repoPath, filePath, stage: 1);
        var oursBytes = GitCliHelpers.ReadConflictStageBytes(repoPath, filePath, stage: 2);
        var theirsBytes = GitCliHelpers.ReadConflictStageBytes(repoPath, filePath, stage: 3);

        return new ImageConflictPayload(
            FilePath: filePath,
            Ours: Classify(oursBytes),
            Theirs: Classify(theirsBytes),
            Base: Classify(baseBytes));
    }

    /// <summary>
    /// Sniff the magic bytes. Public for testing.
    /// </summary>
    internal static ImageSidePayload Classify(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return new ImageSidePayload(null, ImageFormat.None, IsLfsPointer: false);

        if (IsLfs(bytes))
            return new ImageSidePayload(bytes, ImageFormat.Unknown, IsLfsPointer: true);

        return new ImageSidePayload(bytes, DetectFormat(bytes), IsLfsPointer: false);
    }

    private static bool IsLfs(byte[] bytes)
    {
        if (bytes.Length < LfsPointerPrefix.Length) return false;
        for (int i = 0; i < LfsPointerPrefix.Length; i++)
        {
            if (bytes[i] != LfsPointerPrefix[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Magic-byte sniff — doesn't trust extensions. Covers the image formats
    /// an engineering team is likely to commit: PNG, JPEG, GIF, BMP, WebP.
    /// SVG is XML text (non-binary) and doesn't reach this code path.
    /// </summary>
    private static ImageFormat DetectFormat(byte[] bytes)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return ImageFormat.Png;

        // JPEG: FF D8 FF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ImageFormat.Jpeg;

        // GIF: "GIF87a" or "GIF89a"
        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
            bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
            return ImageFormat.Gif;

        // BMP: "BM"
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return ImageFormat.Bmp;

        // WebP: "RIFF" + 4 size bytes + "WEBP"
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ImageFormat.Webp;

        return ImageFormat.Unknown;
    }
}
