using System.Collections.ObjectModel;

namespace Leaf.Models;

/// <summary>
/// Tree node for displaying file changes in a hierarchical view.
/// Similar to PathTreeNode but uses FileChangeInfo instead of FileStatusInfo.
/// </summary>
public class FileChangeTreeNode
{
    public FileChangeTreeNode(string name, string relativePath, bool isFile, FileChangeInfo? file = null, bool isRoot = false)
    {
        Name = name;
        RelativePath = relativePath;
        IsFile = isFile;
        File = file;
        IsRoot = isRoot;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public bool IsFile { get; }
    public bool IsRoot { get; }
    public FileChangeInfo? File { get; }
    public ObservableCollection<FileChangeTreeNode> Children { get; } = [];
}
