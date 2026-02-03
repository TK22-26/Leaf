namespace Leaf.Services;

/// <summary>
/// Abstraction for file system operations (Explorer, default apps) to enable testability.
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Opens Windows Explorer at the specified path.
    /// </summary>
    /// <param name="path">The directory or file path to open.</param>
    void OpenInExplorer(string path);

    /// <summary>
    /// Opens Windows Explorer and selects the specified file.
    /// </summary>
    /// <param name="filePath">The file path to select.</param>
    void OpenInExplorerAndSelect(string filePath);

    /// <summary>
    /// Opens a file using its default associated application.
    /// </summary>
    /// <param name="filePath">The file path to open.</param>
    void OpenWithDefaultApp(string filePath);

    /// <summary>
    /// Opens Windows Explorer at the specified directory.
    /// </summary>
    /// <param name="directoryPath">The directory path to reveal.</param>
    void RevealInExplorer(string directoryPath);
}
