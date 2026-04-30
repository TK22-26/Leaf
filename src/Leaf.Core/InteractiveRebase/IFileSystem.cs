namespace Leaf.Core.InteractiveRebase;

/// <summary>
/// Minimal filesystem seam used by <see cref="RebaseEditorRunner"/> so its
/// behaviour is unit-testable without touching the real disk.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
    void AppendAllText(string path, string contents);
}

/// <summary>Default <see cref="IFileSystem"/> backed by <see cref="System.IO.File"/>.</summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => System.IO.File.Exists(path);
    public bool DirectoryExists(string path) => System.IO.Directory.Exists(path);
    public string ReadAllText(string path) => System.IO.File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => System.IO.File.WriteAllText(path, contents);
    public void AppendAllText(string path, string contents) => System.IO.File.AppendAllText(path, contents);
}

/// <summary>
/// Minimal environment seam matching <see cref="IFileSystem"/>. Real impl
/// reads from <see cref="System.Environment"/>; tests substitute a dictionary.
/// </summary>
public interface IEnvironment
{
    string? GetVariable(string name);
}

/// <summary>Default <see cref="IEnvironment"/> backed by <see cref="System.Environment"/>.</summary>
public sealed class RealEnvironment : IEnvironment
{
    public string? GetVariable(string name) => System.Environment.GetEnvironmentVariable(name);
}
