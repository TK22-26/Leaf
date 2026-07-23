using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Services.Git;

/// <summary>
/// Integration tests for issue #40 against a real temp repository:
/// commits reachable only from a tag (their branch was deleted) must
/// still appear in the history walk, and annotated tags must decorate
/// the commit — not the tag object's own SHA.
/// </summary>
public sealed class CommitHistoryTagSeedTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitService _git = new();

    public CommitHistoryTagSeedTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), "leaf-tagseed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repoPath);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@leaf.local");
        Git("config", "user.name", "Leaf Test");
        Commit("initial");
    }

    public void Dispose()
    {
        try
        {
            // .git objects are read-only; clear attributes before delete.
            foreach (var file in Directory.EnumerateFiles(_repoPath, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_repoPath, recursive: true);
        }
        catch (IOException) { /* best-effort cleanup of temp dir */ }
        catch (UnauthorizedAccessException) { /* best-effort cleanup of temp dir */ }
    }

    private void Git(params string[] args)
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
        var stderr = p.StandardError.ReadToEnd();
        _ = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        p.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} should succeed: {stderr}");
    }

    private void Commit(string message)
    {
        File.AppendAllText(Path.Combine(_repoPath, "file.txt"), message + Environment.NewLine);
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    [Fact]
    public async Task LightweightTagOnDeletedBranch_CommitStillInHistory()
    {
        Git("checkout", "-q", "-b", "topic");
        Commit("terminal commit");
        Git("tag", "v-light");
        Git("checkout", "-q", "main");
        Git("branch", "-D", "topic");

        var history = await _git.GetCommitHistoryAsync(_repoPath);

        var tagged = history.SingleOrDefault(c => c.TagNames.Contains("v-light"));
        tagged.Should().NotBeNull("the tag-only commit must be seeded into the walk");
        tagged!.MessageShort.Should().Be("terminal commit");
    }

    [Fact]
    public async Task AnnotatedTagOnDeletedBranch_CommitInHistoryAndDecorated()
    {
        Git("checkout", "-q", "-b", "topic");
        Commit("annotated terminal");
        Git("tag", "-a", "v-annot", "-m", "release notes");
        Git("checkout", "-q", "main");
        Git("branch", "-D", "topic");

        var history = await _git.GetCommitHistoryAsync(_repoPath);

        // Peeling matters here: the annotated tag's own object SHA never
        // matches a commit, so pre-#40 the decoration (and the commit,
        // once the branch was gone) vanished entirely.
        var tagged = history.SingleOrDefault(c => c.TagNames.Contains("v-annot"));
        tagged.Should().NotBeNull();
        tagged!.MessageShort.Should().Be("annotated terminal");
    }

    [Fact]
    public async Task AnnotatedTagOnBranchTip_DecoratesTheCommit()
    {
        Commit("tip commit");
        Git("tag", "-a", "v-on-branch", "-m", "notes");

        var history = await _git.GetCommitHistoryAsync(_repoPath);

        history.Single(c => c.MessageShort == "tip commit")
            .TagNames.Should().Contain("v-on-branch");
    }

    [Fact]
    public async Task NoTags_HistoryUnchanged()
    {
        Commit("second");

        var history = await _git.GetCommitHistoryAsync(_repoPath);

        history.Select(c => c.MessageShort).Should().Equal("second", "initial");
    }
}
