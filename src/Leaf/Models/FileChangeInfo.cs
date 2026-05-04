namespace Leaf.Models;

/// <summary>
/// Type of file change in a commit.
/// </summary>
public enum FileChangeStatus
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Untracked,
    Ignored,
    Conflicted,
    Unchanged
}

/// <summary>
/// POCO representing a file change in a commit.
/// </summary>
public class FileChangeInfo
{
    /// <summary>
    /// Path to the file (relative to repo root).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Old path if renamed/copied.
    /// </summary>
    public string? OldPath { get; set; }

    /// <summary>
    /// Type of change.
    /// </summary>
    public FileChangeStatus Status { get; set; }

    /// <summary>
    /// Number of lines added.
    /// </summary>
    public int LinesAdded { get; set; }

    /// <summary>
    /// Number of lines deleted.
    /// </summary>
    public int LinesDeleted { get; set; }

    /// <summary>
    /// True if this is a binary file.
    /// </summary>
    public bool IsBinary { get; set; }

    /// <summary>
    /// True when this "file" is actually a submodule pointer
    /// (git-link mode). The traditional line-diff has no meaning for
    /// these entries — the interesting change is the commit SHA the
    /// parent records for the submodule.
    /// </summary>
    public bool IsSubmodule { get; set; }

    /// <summary>
    /// Submodule commit SHA recorded in the parent tree before this
    /// commit. Empty for submodules being added.
    /// </summary>
    public string SubmoduleOldSha { get; set; } = string.Empty;

    /// <summary>
    /// Submodule commit SHA recorded in the parent tree at this commit.
    /// Empty for submodules being removed.
    /// </summary>
    public string SubmoduleNewSha { get; set; } = string.Empty;

    /// <summary>
    /// File name (without path).
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// Directory path (without file name).
    /// </summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>
    /// File extension (including the dot, e.g., ".cs").
    /// </summary>
    public string Extension => System.IO.Path.GetExtension(Path);

    /// <summary>
    /// Status indicator character for display.
    /// </summary>
    public string StatusIndicator => Status switch
    {
        FileChangeStatus.Added => "+",
        FileChangeStatus.Modified => "M",
        FileChangeStatus.Deleted => "-",
        FileChangeStatus.Renamed => "R",
        FileChangeStatus.Copied => "C",
        FileChangeStatus.TypeChanged => "T",
        FileChangeStatus.Untracked => "?",
        FileChangeStatus.Ignored => "!",
        FileChangeStatus.Conflicted => "!",
        FileChangeStatus.Unchanged => " ",
        _ => " "
    };
}
