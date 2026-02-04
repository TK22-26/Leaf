using System.Collections;
using System.Windows.Input;

namespace Leaf.Models;

/// <summary>
/// Context object bundling all commands, data sources, and metadata for a file changes section.
/// Used by FileChangesSectionControl to reduce binding noise in XAML.
/// </summary>
public class FileChangesSectionContext
{
    /// <summary>
    /// Section display title (e.g., "Unstaged", "Staged").
    /// </summary>
    public required string SectionTitle { get; init; }

    /// <summary>
    /// True if this is the staged section, false for unstaged.
    /// </summary>
    public required bool IsStagedSection { get; init; }

    /// <summary>
    /// Data source for the flat file list view.
    /// </summary>
    public required IEnumerable FilesSource { get; init; }

    /// <summary>
    /// Data source for the tree view.
    /// </summary>
    public required IEnumerable TreeItemsSource { get; init; }

    /// <summary>
    /// Primary action command (Stage or Unstage) for individual files.
    /// </summary>
    public required ICommand PrimaryActionCommand { get; init; }

    /// <summary>
    /// Primary action button text (e.g., "Stage", "Unstage").
    /// </summary>
    public required string PrimaryActionText { get; init; }

    /// <summary>
    /// Bulk action command (StageAll or UnstageAll).
    /// </summary>
    public required ICommand BulkActionCommand { get; init; }

    /// <summary>
    /// Bulk action button text (e.g., "Stage All", "Unstage All").
    /// </summary>
    public required string BulkActionText { get; init; }

    /// <summary>
    /// Command to discard changes for a file.
    /// </summary>
    public required ICommand DiscardFileCommand { get; init; }

    /// <summary>
    /// Command to add a file to .gitignore.
    /// </summary>
    public required ICommand IgnoreFileCommand { get; init; }

    /// <summary>
    /// Command to add all files with a specific extension to .gitignore.
    /// </summary>
    public required ICommand IgnoreExtensionCommand { get; init; }

    /// <summary>
    /// Command to add all files in a directory to .gitignore.
    /// </summary>
    public required ICommand IgnoreDirectoryCommand { get; init; }

    /// <summary>
    /// Command to stash a single file.
    /// </summary>
    public required ICommand StashFileCommand { get; init; }

    /// <summary>
    /// Command to open a file with the default application.
    /// </summary>
    public required ICommand OpenFileCommand { get; init; }

    /// <summary>
    /// Command to open the file in Windows Explorer.
    /// </summary>
    public required ICommand OpenInExplorerCommand { get; init; }

    /// <summary>
    /// Command to copy the file path to clipboard.
    /// </summary>
    public required ICommand CopyFilePathCommand { get; init; }

    /// <summary>
    /// Command to delete a file from the filesystem.
    /// </summary>
    public required ICommand DeleteFileCommand { get; init; }

    /// <summary>
    /// Command to delete a Windows reserved filename using admin privileges.
    /// Only applicable for unstaged section.
    /// </summary>
    public ICommand? AdminDeleteCommand { get; init; }

    /// <summary>
    /// Command to show the diff for the selected file.
    /// </summary>
    public required ICommand FileSelectedCommand { get; init; }
}
