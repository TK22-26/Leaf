using System.Collections.ObjectModel;

namespace Leaf.Models;

public class PathTreeNode
{
    public PathTreeNode(string name, string relativePath, bool isFile, FileStatusInfo? file = null)
    {
        Name = name;
        RelativePath = relativePath;
        IsFile = isFile;
        File = file;
    }

    public string Name { get; }
    public string RelativePath { get; }
    public bool IsFile { get; }
    public FileStatusInfo? File { get; }
    public ObservableCollection<PathTreeNode> Children { get; } = [];
}
