using Leaf.Models;
using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Fake gitignore service — every ignore op is a no-op. Tests that care
/// about file-level ignore behavior should swap in a richer fake.
/// </summary>
public class FakeGitignoreService : IGitignoreService
{
    public Task IgnoreFileAsync(string repoPath, FileStatusInfo file) => Task.CompletedTask;
    public Task IgnoreExtensionAsync(string repoPath, FileStatusInfo file) => Task.CompletedTask;
    public Task IgnoreDirectoryAsync(string repoPath, FileStatusInfo file) => Task.CompletedTask;
    public Task IgnoreDirectoryPathAsync(string repoPath, string directoryPath, IEnumerable<FileStatusInfo> trackedFiles) => Task.CompletedTask;
}
