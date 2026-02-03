using System;
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
                SelectedRepository.Path, commitSha, file.Path);

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
    /// Handle hunk reverted event from the diff viewer - refresh working changes.
    /// </summary>
    private async void OnDiffViewerHunkReverted(object? sender, Models.DiffHunk hunk)
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
                SelectedRepository.Path, file.Path);

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
                SelectedRepository.Path, file.Path);

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
