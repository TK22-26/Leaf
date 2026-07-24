#nullable enable
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Integration;

/// <summary>
/// End-to-end regression for the "branches disappear" bug: when a repo's
/// index holds an <em>orphaned gitlink</em> — a mode-160000 entry with no
/// matching <c>.gitmodules</c> mapping (classically a nested worktree or
/// repo accidentally <c>git add</c>ed, e.g. a Claude Code agent worktree
/// under <c>.claude/</c>) — <c>git submodule status</c> aborts wholesale.
/// Leaf used to let that abort bubble out of <see cref="GitService.GetSubmodulesAsync"/>
/// and blank the ENTIRE branch sidebar. This fixture reproduces the exact
/// on-disk shape (one registered submodule + one orphaned gitlink) and
/// verifies enumeration now degrades gracefully: it lists the registered
/// submodule from the index and silently drops the orphan instead of
/// throwing. Mirrors the temp-repo fixture pattern of the other
/// <c>*EndToEndTests</c> — same git-on-PATH precondition.
/// </summary>
public class SubmoduleOrphanedGitlinkTests : IDisposable
{
    private readonly string _repoPath;
    private readonly List<string> _toCleanup = new();

    public SubmoduleOrphanedGitlinkTests()
    {
        _repoPath = CreateTempRepo("leaf-submodule-orphan-e2e");
        _toCleanup.Add(_repoPath);
    }

    public void Dispose()
    {
        foreach (var path in _toCleanup)
        {
            TryDeleteDirectory(path);
        }
    }

    [Fact]
    public async Task GetSubmodules_WithOrphanedGitlink_ListsRegisteredAndDropsOrphan()
    {
        // Parent repo needs an initial commit before we can attach anything.
        File.WriteAllText(Path.Combine(_repoPath, "readme.txt"), "parent\n");
        RunGit(_repoPath, "add", "readme.txt");
        RunGit(_repoPath, "commit", "-m", "init parent");

        // A real, registered submodule (writes .gitmodules + a gitlink and
        // clones the working tree). Local-path submodules require the
        // protocol allowlist on modern git.
        var innerRepo = CreateTempRepo("leaf-submodule-inner");
        _toCleanup.Add(innerRepo);
        File.WriteAllText(Path.Combine(innerRepo, "lib.txt"), "lib\n");
        RunGit(innerRepo, "add", "lib.txt");
        RunGit(innerRepo, "commit", "-m", "init inner");

        RunGit(_repoPath, "-c", "protocol.file.allow=always",
            "submodule", "add", Uri(innerRepo), "sub");
        RunGit(_repoPath, "commit", "-m", "add registered submodule");

        // Now the accidental orphan: a nested repo staged as a gitlink with
        // NO .gitmodules entry. `git add` on a dir containing its own .git
        // records a mode-160000 entry (and prints an "embedded repository"
        // warning, which we tolerate).
        var orphanDir = Path.Combine(_repoPath, "orphan");
        Directory.CreateDirectory(orphanDir);
        RunGit(orphanDir, "init", "-b", "main");
        RunGit(orphanDir, "config", "user.email", "test@leaf.dev");
        RunGit(orphanDir, "config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(orphanDir, "data.txt"), "orphan\n");
        RunGit(orphanDir, "add", "data.txt");
        RunGit(orphanDir, "commit", "-m", "init orphan");
        RunGitAllowFail(_repoPath, "add", "orphan"); // warns; still stages the gitlink
        RunGit(_repoPath, "commit", "-m", "accidental gitlink");

        // Precondition: `git submodule status` really does abort here, so
        // the assertions below prove the fallback path — not the porcelain —
        // is what produced the result.
        var (code, _, stderr) = RunGitAllowFail(_repoPath, "submodule", "status");
        code.Should().NotBe(0, "the orphaned gitlink should make the porcelain abort");
        stderr.Should().Contain("no submodule mapping",
            "this is the exact failure the fallback is designed to survive");

        // ACT — the call that used to throw and blank the sidebar.
        var service = CreateService();
        var submodules = await service.GetSubmodulesAsync(_repoPath);

        // ASSERT — the registered submodule survives; the orphan is dropped.
        submodules.Should().ContainSingle(s => s.Path == "sub",
            "the registered submodule must still be listed via the index fallback");
        submodules.Should().NotContain(s => s.Path == "orphan",
            "an orphaned gitlink is not a submodule and must not appear");

        var sub = submodules.Single();
        sub.Name.Should().Be("sub");
        sub.Status.Should().Be(SubmoduleStatus.UpToDate,
            "the working tree was just checked out at the recorded commit");
        sub.RecordedSha.Should().NotBeNullOrWhiteSpace();
        sub.WorkingSha.Should().Be(sub.RecordedSha);
        sub.IsInitialized.Should().BeTrue();
    }

    // ---- harness (duplicated per-file to keep the fixture self-contained) --

    private static GitService CreateService() => new(new GitCommandRunner());

    private static string CreateTempRepo(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-b", "main");
        RunGit(path, "config", "user.email", "test@leaf.dev");
        RunGit(path, "config", "user.name", "Test User");
        RunGit(path, "config", "core.autocrlf", "false");
        RunGit(path, "config", "advice.detachedHead", "false");
        return path;
    }

    /// <summary>Forward-slashed absolute path — git accepts it as a local URL on every platform.</summary>
    private static string Uri(string path) => path.Replace('\\', '/');

    private static void TryDeleteDirectory(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (IOException) { Thread.Sleep(100 * (attempt + 1)); }
            catch (UnauthorizedAccessException) { Thread.Sleep(100 * (attempt + 1)); }
        }
    }

    private static (int exitCode, string stdout, string stderr) RunGitAllowFail(
        string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static void RunGit(string workingDir, params string[] args)
    {
        var (code, _, stderr) = RunGitAllowFail(workingDir, args);
        if (code != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed (exit {code}): {stderr.Trim()}");
    }
}
