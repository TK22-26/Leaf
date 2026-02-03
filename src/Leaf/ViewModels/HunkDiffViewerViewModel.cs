using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the hunk-based diff viewer control.
/// Displays individual change hunks with revert/stage capabilities.
/// </summary>
public partial class HunkDiffViewerViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IHunkService _hunkService;

    public HunkDiffViewerViewModel(IGitService gitService, IHunkService hunkService)
    {
        _gitService = gitService;
        _hunkService = hunkService;
    }

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _repositoryPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    private ObservableCollection<DiffHunk> _hunks = [];

    [ObservableProperty]
    private int _linesAdded;

    [ObservableProperty]
    private int _linesDeleted;

    [ObservableProperty]
    private bool _isBinary;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private IHighlightingDefinition? _syntaxHighlighting;

    /// <summary>
    /// True if there are any hunks to display.
    /// </summary>
    public bool HasChanges => Hunks.Count > 0;

    /// <summary>
    /// Event raised when a hunk has been reverted successfully.
    /// </summary>
    public event EventHandler<DiffHunk>? HunkReverted;

    /// <summary>
    /// Event raised when a hunk has been staged successfully.
    /// </summary>
    public event EventHandler<DiffHunk>? HunkStaged;

    /// <summary>
    /// Event raised when a hunk has been unstaged successfully.
    /// </summary>
    public event EventHandler<DiffHunk>? HunkUnstaged;

    /// <summary>
    /// Event raised when the viewer should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Load a diff result and parse it into hunks.
    /// </summary>
    public void LoadDiff(FileDiffResult diffResult, string repositoryPath)
    {
        FileName = diffResult.FileName;
        FilePath = diffResult.FilePath;
        RepositoryPath = repositoryPath;
        IsBinary = diffResult.IsBinary;
        LinesAdded = diffResult.LinesAddedCount;
        LinesDeleted = diffResult.LinesDeletedCount;
        ErrorMessage = null;

        // Set syntax highlighting based on file extension
        var extension = Path.GetExtension(diffResult.FileName);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(extension);

        // Parse diff into hunks
        var parsedHunks = _hunkService.ParseHunks(diffResult);
        Hunks = new ObservableCollection<DiffHunk>(parsedHunks);
    }

    /// <summary>
    /// Clear the current diff.
    /// </summary>
    public void Clear()
    {
        FileName = string.Empty;
        FilePath = string.Empty;
        RepositoryPath = string.Empty;
        Hunks = [];
        LinesAdded = 0;
        LinesDeleted = 0;
        IsBinary = false;
        ErrorMessage = null;
        SyntaxHighlighting = null;
    }

    /// <summary>
    /// Revert a specific hunk (discard changes in working directory).
    /// </summary>
    [RelayCommand]
    public async Task RevertHunkAsync(DiffHunk hunk)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || string.IsNullOrEmpty(FilePath))
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var patch = _hunkService.GenerateHunkPatch(FilePath, hunk);
            await _gitService.RevertHunkAsync(RepositoryPath, patch);

            HunkReverted?.Invoke(this, hunk);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to revert hunk: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Stage a specific hunk (add to index).
    /// </summary>
    [RelayCommand]
    public async Task StageHunkAsync(DiffHunk hunk)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || string.IsNullOrEmpty(FilePath))
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var patch = _hunkService.GenerateHunkPatch(FilePath, hunk);
            await _gitService.StageHunkAsync(RepositoryPath, patch);

            HunkStaged?.Invoke(this, hunk);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to stage hunk: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Unstage a specific hunk (remove from index).
    /// </summary>
    [RelayCommand]
    public async Task UnstageHunkAsync(DiffHunk hunk)
    {
        if (string.IsNullOrEmpty(RepositoryPath) || string.IsNullOrEmpty(FilePath))
            return;

        try
        {
            IsLoading = true;
            ErrorMessage = null;

            var patch = _hunkService.GenerateHunkPatch(FilePath, hunk);
            await _gitService.UnstageHunkAsync(RepositoryPath, patch);

            HunkUnstaged?.Invoke(this, hunk);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to unstage hunk: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Request to close the viewer.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
