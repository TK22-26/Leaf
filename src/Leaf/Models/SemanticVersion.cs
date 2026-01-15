using System.Text.RegularExpressions;

namespace Leaf.Models;

/// <summary>
/// Represents a semantic version (Major.Minor.Patch[-Prerelease][+Build]).
/// Follows the Semantic Versioning 2.0.0 specification.
/// </summary>
public partial class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    /// <summary>
    /// Major version number - incremented for breaking changes.
    /// </summary>
    public int Major { get; set; }

    /// <summary>
    /// Minor version number - incremented for new features.
    /// </summary>
    public int Minor { get; set; }

    /// <summary>
    /// Patch version number - incremented for bug fixes.
    /// </summary>
    public int Patch { get; set; }

    /// <summary>
    /// Optional prerelease identifier (e.g., "alpha", "beta.1", "rc.2").
    /// </summary>
    public string? Prerelease { get; set; }

    /// <summary>
    /// Optional build metadata (e.g., "build.123", "20240115").
    /// </summary>
    public string? BuildMetadata { get; set; }

    /// <summary>
    /// Creates a new SemanticVersion with default values (0.0.0).
    /// </summary>
    public SemanticVersion() { }

    /// <summary>
    /// Creates a new SemanticVersion with the specified values.
    /// </summary>
    public SemanticVersion(int major, int minor, int patch, string? prerelease = null, string? buildMetadata = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        BuildMetadata = buildMetadata;
    }

    /// <summary>
    /// Attempts to parse a version string into a SemanticVersion.
    /// Supports formats: "1.2.3", "v1.2.3", "1.2.3-beta", "1.2.3-rc.1+build.456"
    /// </summary>
    public static SemanticVersion? TryParse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        // Remove leading 'v' or 'V' if present
        var cleanVersion = version.TrimStart('v', 'V').Trim();

        var match = SemVerRegex().Match(cleanVersion);
        if (!match.Success)
            return null;

        return new SemanticVersion
        {
            Major = int.Parse(match.Groups["major"].Value),
            Minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : 0,
            Patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0,
            Prerelease = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null,
            BuildMetadata = match.Groups["build"].Success ? match.Groups["build"].Value : null
        };
    }

    /// <summary>
    /// Creates a new version with the major number incremented and minor/patch reset to 0.
    /// </summary>
    public SemanticVersion BumpMajor() => new(Major + 1, 0, 0);

    /// <summary>
    /// Creates a new version with the minor number incremented and patch reset to 0.
    /// </summary>
    public SemanticVersion BumpMinor() => new(Major, Minor + 1, 0);

    /// <summary>
    /// Creates a new version with the patch number incremented.
    /// </summary>
    public SemanticVersion BumpPatch() => new(Major, Minor, Patch + 1);

    /// <summary>
    /// Creates a new version with the specified prerelease identifier.
    /// </summary>
    public SemanticVersion WithPrerelease(string prerelease) =>
        new(Major, Minor, Patch, prerelease, BuildMetadata);

    /// <summary>
    /// Creates a new version without prerelease or build metadata (stable release).
    /// </summary>
    public SemanticVersion ToStable() => new(Major, Minor, Patch);

    /// <summary>
    /// Returns the version string without the 'v' prefix.
    /// </summary>
    public override string ToString()
    {
        var result = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Prerelease))
            result += $"-{Prerelease}";
        if (!string.IsNullOrEmpty(BuildMetadata))
            result += $"+{BuildMetadata}";
        return result;
    }

    /// <summary>
    /// Returns the version string with the specified prefix.
    /// </summary>
    public string ToString(string prefix) => $"{prefix}{ToString()}";

    /// <summary>
    /// Compares two versions according to semantic versioning rules.
    /// </summary>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var majorCompare = Major.CompareTo(other.Major);
        if (majorCompare != 0) return majorCompare;

        var minorCompare = Minor.CompareTo(other.Minor);
        if (minorCompare != 0) return minorCompare;

        var patchCompare = Patch.CompareTo(other.Patch);
        if (patchCompare != 0) return patchCompare;

        // Prerelease versions have lower precedence than normal versions
        if (string.IsNullOrEmpty(Prerelease) && !string.IsNullOrEmpty(other.Prerelease))
            return 1;
        if (!string.IsNullOrEmpty(Prerelease) && string.IsNullOrEmpty(other.Prerelease))
            return -1;

        return string.Compare(Prerelease, other.Prerelease, StringComparison.Ordinal);
    }

    public bool Equals(SemanticVersion? other)
    {
        if (other is null) return false;
        return Major == other.Major &&
               Minor == other.Minor &&
               Patch == other.Patch &&
               Prerelease == other.Prerelease;
    }

    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) =>
        !(left == right);

    public static bool operator <(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) =>
        left is null || left.CompareTo(right) <= 0;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) =>
        left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    [GeneratedRegex(@"^(?<major>\d+)(\.(?<minor>\d+))?(\.(?<patch>\d+))?(-(?<prerelease>[0-9A-Za-z\-\.]+))?(\+(?<build>[0-9A-Za-z\-\.]+))?$")]
    private static partial Regex SemVerRegex();
}
