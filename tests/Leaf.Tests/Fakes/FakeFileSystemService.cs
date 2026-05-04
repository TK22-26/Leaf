using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Minimal fake file-system service — all operations are no-ops; nothing
/// touches the actual shell.
/// </summary>
public class FakeFileSystemService : IFileSystemService
{
    public void OpenInExplorer(string folderPath) { }
    public void OpenInExplorerAndSelect(string filePath) { }
    public void RevealInExplorer(string path) { }
    public void OpenWithDefaultApp(string filePath) { }
    public void OpenInTerminal(string folderPath) { }
}
