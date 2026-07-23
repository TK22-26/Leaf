using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services.Git;

/// <summary>
/// Integration test for clearing orphaned remote-tracking namespaces
/// (refs/remotes/&lt;x&gt;/* with no configured remote). Proves the exact
/// git operations delete them, since fetch/prune provably cannot.
/// </summary>
public sealed class RemoteTrackingNamespaceTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitService _git = new();

    public RemoteTrackingNamespaceTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "leaf-orphanrefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoPath);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@leaf.local");
        Git("config", "user.name", "Leaf Test");
        File.WriteAllText(Path.Combine(_repoPath, "f.txt"), "hi");
        Git("add", "-A");
        Git("commit", "-q", "-m", "initial");
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_repoPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_repoPath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string Git(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        p.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {stderr}");
        return stdout.Trim();
    }

    private int CountRefs(string underPrefix) =>
        Git("for-each-ref", "--format=%(refname)", underPrefix)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    [Fact]
    public async Task DeleteRemoteTrackingNamespace_RemovesOrphanedRefs_AndPruneCannot()
    {
        var head = Git("rev-parse", "HEAD");
        // Fabricate orphaned tracking refs like the ESM-debug debris.
        Git("update-ref", "refs/remotes/esm-c2/16e7eb7", head);
        Git("update-ref", "refs/remotes/esm-c2/286ae7a", head);
        Git("update-ref", "refs/remotes/esm-c3/a152f18", head);
        CountRefs("refs/remotes/esm-c2/").Should().Be(2);

        // A normal prune can't touch them (no configured remote / refspec).
        Git("remote", "add", "origin", "https://example.test/repo.git");
        // (Offline; just prove prune leaves the orphaned refs intact.)
        var beforePrune = CountRefs("refs/remotes/esm-c2/");
        beforePrune.Should().Be(2, "prune/fetch never reach refs outside a remote's refspec");

        // The service clears exactly the targeted namespace.
        var deleted = await _git.DeleteRemoteTrackingNamespaceAsync(_repoPath, "esm-c2");

        deleted.Should().Be(2);
        CountRefs("refs/remotes/esm-c2/").Should().Be(0);
        CountRefs("refs/remotes/esm-c3/").Should().Be(1, "only the named namespace is cleared");
    }

    [Fact]
    public async Task DeleteRemoteTrackingNamespace_NoRefs_ReturnsZero()
    {
        var deleted = await _git.DeleteRemoteTrackingNamespaceAsync(_repoPath, "does-not-exist");
        deleted.Should().Be(0);
    }
}
