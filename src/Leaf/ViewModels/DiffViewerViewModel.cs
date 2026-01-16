using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Highlighting;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the diff viewer control.
/// </summary>
public partial class DiffViewerViewModel : ObservableObject
{
    [ObservableProperty]
    private FileDiffResult? _diffResult;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _inlineContent = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DiffLine> _lines = [];

    [ObservableProperty]
    private bool _isBinary;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _linesAdded;

    [ObservableProperty]
    private int _linesDeleted;

    [ObservableProperty]
    private IHighlightingDefinition? _syntaxHighlighting;

    /// <summary>
    /// Event raised when the diff viewer should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Load a diff result into the viewer.
    /// </summary>
    public void LoadDiff(FileDiffResult result)
    {
        DiffResult = result;
        FileName = result.FileName;
        FilePath = result.FilePath;
        InlineContent = result.InlineContent;
        Lines = result.Lines;
        IsBinary = result.IsBinary;
        LinesAdded = result.LinesAddedCount;
        LinesDeleted = result.LinesDeletedCount;

        // Set syntax highlighting based on file extension
        var extension = Path.GetExtension(result.FileName);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }

    /// <summary>
    /// Clear the current diff.
    /// </summary>
    public void Clear()
    {
        DiffResult = null;
        FileName = string.Empty;
        FilePath = string.Empty;
        InlineContent = string.Empty;
        Lines = [];
        IsBinary = false;
        LinesAdded = 0;
        LinesDeleted = 0;
        SyntaxHighlighting = null;
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
