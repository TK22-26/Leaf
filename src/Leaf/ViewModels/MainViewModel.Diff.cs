using System;
using System.IO;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Diff viewer operations.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Show the diff viewer for a file in a commit.
    /// </summary>
    public async Task ShowFileDiffAsync(Models.FileChangeInfo file, string commitSha)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            // Get the file content from the commit
            var (oldContent, newContent) = await _gitService.GetFileDiffAsync(
                SelectedRepository.Path, commitSha, file.Path, cancellationToken: CurrentRepositoryToken);

            // Compute the diff
            var diffService = new Services.DiffService();
            var result = diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;

            // Load into the view model
            DiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    /// <summary>
    /// Close the diff viewer.
    /// </summary>
    public void CloseDiffViewer()
    {
        IsDiffViewerVisible = false;
        DiffViewerViewModel?.Clear();
    }

    /// <summary>
    /// Handle file deleted or discarded - close diff viewer if it's showing the affected file.
    /// </summary>
    private void OnFileDeletedOrDiscarded(object? sender, FileDeletedOrDiscardedEventArgs e)
    {
        if (!IsDiffViewerVisible || DiffViewerViewModel == null)
            return;

        if (e.AffectsAllFiles)
        {
            CloseDiffViewer();
            return;
        }

        if (!string.IsNullOrEmpty(e.FilePath) &&
            string.Equals(
                e.FilePath?.Replace('\\', '/'),
                DiffViewerViewModel.FilePath?.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
        {
            CloseDiffViewer();
        }
    }

    /// <summary>
    /// Handle hunk reverted event from the diff viewer - refresh working changes.
    /// </summary>
    private async void OnDiffViewerHunkReverted(object? sender, Models.DiffHunk hunk)
    {
        try
        {
            // Refresh working changes after reverting a hunk
            if (GitGraphViewModel != null && SelectedRepository != null)
            {
                await GitGraphViewModel.RefreshWorkingChangesAsync();

                if (WorkingChangesViewModel != null && IsWorkingChangesSelected)
                {
                    WorkingChangesViewModel.SetWorkingChanges(
                        SelectedRepository.Path,
                        GitGraphViewModel.WorkingChanges);
                }
            }

            // Note: We don't close the diff viewer - the user can continue viewing/reverting other hunks
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OnDiffViewerHunkReverted), isUserAction: true);
        }
    }

    /// <summary>
    /// Handle file selected event from working changes view.
    /// </summary>
    private async void OnWorkingChangesFileSelected(object? sender, FileSelectedEventArgs e)
    {
        try
        {
            if (e.IsStaged)
            {
                await ShowStagedFileDiffAsync(e.File);
            }
            else
            {
                await ShowUnstagedFileDiffAsync(e.File);
            }
        }
        catch (Exception ex)
        {
            AsyncErrorHandler.Handle(ex, nameof(OnWorkingChangesFileSelected), isUserAction: true);
        }
    }

    private void OnPullRequestFileSelected(object? sender, PullRequestFileInfo file)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        if (string.IsNullOrWhiteSpace(file.PatchContent))
        {
            _notificationService?.Show(
                "Diff unavailable",
                $"Leaf could not load the diff for {file.FileName}.",
                NotificationType.Warning);
            return;
        }

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var diffResult = BuildPullRequestPatchResult(file.PatchContent, file.Path);
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;
            DiffViewerViewModel.LoadDiff(diffResult);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load pull request diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    private static FileDiffResult BuildPullRequestPatchResult(string patchText, string filePath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(filePath)
            ? "pull-request.diff"
            : filePath.Replace('\\', '/').TrimStart('/');

        var result = new FileDiffResult
        {
            FileName = Path.GetFileName(normalizedPath),
            FilePath = normalizedPath,
            IsFileBacked = false
        };

        var inlineLines = new List<string>();
        var inHunkBody = false;
        var added = 0;
        var deleted = 0;

        foreach (var rawLine in patchText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunkBody = true;
                continue;
            }

            if (!inHunkBody)
                continue;

            if (line.StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
                continue;

            var type = DiffLineType.Unchanged;

            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                type = DiffLineType.Added;
                added++;
            }
            else if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal))
            {
                type = DiffLineType.Deleted;
                deleted++;
            }
            else if (!line.StartsWith(" ", StringComparison.Ordinal))
            {
                continue;
            }

            inlineLines.Add(line);
            result.Lines.Add(new DiffLine
            {
                Text = line,
                Type = type
            });
        }

        result.InlineContent = string.Join('\n', inlineLines);
        result.LinesAddedCount = added;
        result.LinesDeletedCount = deleted;
        return result;
    }

    private static FileDiffResult BuildUnifiedDiffResult(string diffText, string title)
    {
        var result = new FileDiffResult
        {
            FileName = title,
            FilePath = title,
            InlineContent = diffText,
            IsFileBacked = false
        };

        int linesAdded = 0;
        int linesDeleted = 0;

        foreach (var rawLine in diffText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var type = DiffLineType.Unchanged;

            if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                type = DiffLineType.Added;
                linesAdded++;
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                type = DiffLineType.Deleted;
                linesDeleted++;
            }
            else if (line.StartsWith("@@"))
            {
                type = DiffLineType.Modified;
            }

            result.Lines.Add(new DiffLine
            {
                Text = line,
                Type = type
            });
        }

        result.LinesAddedCount = linesAdded;
        result.LinesDeletedCount = linesDeleted;

        return result;
    }

    /// <summary>
    /// Show diff for an unstaged file (working directory vs index).
    /// </summary>
    public async Task ShowUnstagedFileDiffAsync(Models.FileStatusInfo file)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var (oldContent, newContent) = await _gitService.GetUnstagedFileDiffAsync(
                SelectedRepository.Path, file.Path, cancellationToken: CurrentRepositoryToken);

            var diffService = new Services.DiffService();
            var result = diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;

            DiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }

    /// <summary>
    /// Show diff for a staged file (index vs HEAD).
    /// </summary>
    public async Task ShowStagedFileDiffAsync(Models.FileStatusInfo file)
    {
        if (SelectedRepository == null || DiffViewerViewModel == null)
            return;

        DiffViewerViewModel.IsLoading = true;
        IsDiffViewerVisible = true;

        try
        {
            var (oldContent, newContent) = await _gitService.GetStagedFileDiffAsync(
                SelectedRepository.Path, file.Path, cancellationToken: CurrentRepositoryToken);

            var diffService = new Services.DiffService();
            var result = diffService.ComputeDiff(oldContent, newContent, file.FileName, file.Path);
            DiffViewerViewModel.RepositoryPath = SelectedRepository.Path;

            DiffViewerViewModel.LoadDiff(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load diff: {ex.Message}";
            IsDiffViewerVisible = false;
        }
        finally
        {
            DiffViewerViewModel.IsLoading = false;
        }
    }
}
