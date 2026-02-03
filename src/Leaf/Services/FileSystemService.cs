using System.Diagnostics;

namespace Leaf.Services;

/// <summary>
/// Production implementation of IFileSystemService using Process.Start.
/// </summary>
public class FileSystemService : IFileSystemService
{
    /// <inheritdoc />
    public void OpenInExplorer(string path)
        => Process.Start("explorer.exe", $"\"{path}\"");

    /// <inheritdoc />
    public void OpenInExplorerAndSelect(string filePath)
        => Process.Start("explorer.exe", $"/select,\"{filePath}\"");

    /// <inheritdoc />
    public void OpenWithDefaultApp(string filePath)
        => Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

    /// <inheritdoc />
    public void RevealInExplorer(string directoryPath)
        => Process.Start("explorer.exe", $"\"{directoryPath}\"");
}
