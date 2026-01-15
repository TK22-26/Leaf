using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Leaf.Models;

/// <summary>
/// Represents the working directory changes (uncommitted files).
/// Separates staged and unstaged files for the staging area UI.
/// </summary>
public partial class WorkingChangesInfo : ObservableObject
{
    /// <summary>
    /// Files that have been modified but not staged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanges))]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(HasUnstagedChanges))]
    private ObservableCollection<FileStatusInfo> _unstagedFiles = [];

    /// <summary>
    /// Files that have been staged for commit.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanges))]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    [NotifyPropertyChangedFor(nameof(HasStagedChanges))]
    private ObservableCollection<FileStatusInfo> _stagedFiles = [];

    /// <summary>
    /// Current branch name.
    /// </summary>
    [ObservableProperty]
    private string _branchName = string.Empty;

    /// <summary>
    /// Total number of changed files (staged + unstaged).
    /// </summary>
    public int TotalChanges => UnstagedFiles.Count + StagedFiles.Count;

    /// <summary>
    /// True if there are any changes (staged or unstaged).
    /// </summary>
    public bool HasChanges => TotalChanges > 0;

    /// <summary>
    /// True if there are unstaged changes.
    /// </summary>
    public bool HasUnstagedChanges => UnstagedFiles.Count > 0;

    /// <summary>
    /// True if there are staged changes.
    /// </summary>
    public bool HasStagedChanges => StagedFiles.Count > 0;

    /// <summary>
    /// Summary text for display (e.g., "3 file changes").
    /// </summary>
    public string Summary => TotalChanges switch
    {
        0 => "No changes",
        1 => "1 file change",
        _ => $"{TotalChanges} file changes"
    };

    /// <summary>
    /// Count of modified files (staged + unstaged).
    /// </summary>
    public int ModifiedCount => UnstagedFiles.Count(f => f.Status == FileChangeStatus.Modified)
                              + StagedFiles.Count(f => f.Status == FileChangeStatus.Modified);

    /// <summary>
    /// Count of added/new files (staged + unstaged).
    /// </summary>
    public int AddedCount => UnstagedFiles.Count(f => f.Status == FileChangeStatus.Added || f.Status == FileChangeStatus.Untracked)
                           + StagedFiles.Count(f => f.Status == FileChangeStatus.Added || f.Status == FileChangeStatus.Untracked);

    /// <summary>
    /// Count of deleted files (staged + unstaged).
    /// </summary>
    public int DeletedCount => UnstagedFiles.Count(f => f.Status == FileChangeStatus.Deleted)
                             + StagedFiles.Count(f => f.Status == FileChangeStatus.Deleted);
}

/// <summary>
/// Represents a single file's status in the working directory.
/// </summary>
public partial class FileStatusInfo : ObservableObject
{
    /// <summary>
    /// Full path to the file relative to repository root.
    /// </summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>
    /// Old path if the file was renamed.
    /// </summary>
    [ObservableProperty]
    private string? _oldPath;

    /// <summary>
    /// Type of change (Added, Modified, Deleted, etc.).
    /// </summary>
    [ObservableProperty]
    private FileChangeStatus _status;

    /// <summary>
    /// True if this file is staged for commit.
    /// </summary>
    [ObservableProperty]
    private bool _isStaged;

    /// <summary>
    /// File name without directory path.
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// Directory portion of the path.
    /// </summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>
    /// File extension (including the dot, e.g., ".cs").
    /// </summary>
    public string Extension => System.IO.Path.GetExtension(Path);

    /// <summary>
    /// Status icon character (Segoe Fluent Icons).
    /// </summary>
    public string StatusIcon => Status switch
    {
        FileChangeStatus.Added => "\uE710",      // Add/Plus
        FileChangeStatus.Modified => "\uE70F",   // Edit
        FileChangeStatus.Deleted => "\uE74D",    // Delete
        FileChangeStatus.Renamed => "\uE8AB",    // Rename
        FileChangeStatus.Untracked => "\uE710",  // Add/Plus (same as Added)
        FileChangeStatus.Conflicted => "\uE7BA", // Warning
        _ => "\uE8A5"                            // Document
    };

    /// <summary>
    /// Status color for the icon.
    /// </summary>
    public string StatusColor => Status switch
    {
        FileChangeStatus.Added => "#28A745",     // Green
        FileChangeStatus.Modified => "#F5A623",  // Amber
        FileChangeStatus.Deleted => "#E81123",   // Red
        FileChangeStatus.Renamed => "#0078D4",   // Blue
        FileChangeStatus.Untracked => "#28A745", // Green (same as Added)
        FileChangeStatus.Conflicted => "#E81123",// Red
        _ => "#6B7280"                           // Gray
    };

    /// <summary>
    /// Status letter for compact display (M, A, D, R, ?).
    /// </summary>
    public string StatusLetter => Status switch
    {
        FileChangeStatus.Added => "A",
        FileChangeStatus.Modified => "M",
        FileChangeStatus.Deleted => "D",
        FileChangeStatus.Renamed => "R",
        FileChangeStatus.Untracked => "?",
        FileChangeStatus.Conflicted => "!",
        _ => " "
    };
}
