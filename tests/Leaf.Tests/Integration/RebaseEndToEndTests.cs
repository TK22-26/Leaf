#nullable enable
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.Integration;

/// <summary>
/// End-to-end tests for the non-interactive rebase path that closes
/// GitHub issue #30. Stands up a real repo on disk, drives
/// <see cref="GitService.RebaseAsync"/> directly (the same verb the
/// branch-context-menu RebaseBranch command goes through via
/// <see cref="IRebaseService"/>), and verifies the on-disk effect.
/// Mirrors the structure of <see cref="MergeEndToEndTests"/> — same
/// temp-repo fixture pattern, same git-on-PATH precondition.
/// </summary>
public class RebaseEndToEndTests : IDisposable
{
    private readonly string _repoPath;
    private readonly List<string> _toCleanup = new();

    public RebaseEndToEndTests()
    {
        _repoPath = CreateTempRepo();
        _toCleanup.Add(_repoPath);
    }

    public void Dispose()
    {
        foreach (var path in _toCleanup)
        {
            TryDeleteDirectory(path);
        }
    }

    private static string CreateTempRepo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leaf-rebase-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-b", "main");
        RunGit(path, "config", "user.email", "test@leaf.dev");
        RunGit(path, "config", "user.name", "Test User");
        RunGit(path, "config", "core.autocrlf", "false");
        // Suppress the "advice.detachedHead" hint when we walk through
        // checkouts; it noises up captured stderr without changing behaviour.
        RunGit(path, "config", "advice.detachedHead", "false");
        return path;
    }

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

    private GitService CreateService() => new(new GitCommandRunner());

    /// <summary>Set up: main has commits A→B; feature branches at A and adds C.</summary>
    private (string aSha, string bSha, string cSha) CreateLinearScenario()
    {
        // A: initial commit on main.
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "line 1\n");
        RunGit(_repoPath, "add", "shared.txt");
        RunGit(_repoPath, "commit", "-m", "A: initial");
        var aSha = RevParse("HEAD");

        // Branch off feature at A.
        RunGit(_repoPath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, "feature.txt"), "feature work\n");
        RunGit(_repoPath, "add", "feature.txt");
        RunGit(_repoPath, "commit", "-m", "C: feature work");
        var cSha = RevParse("HEAD");

        // Back to main, advance with B (touches a different file so no conflict).
        RunGit(_repoPath, "checkout", "main");
        File.WriteAllText(Path.Combine(_repoPath, "main.txt"), "main work\n");
        RunGit(_repoPath, "add", "main.txt");
        RunGit(_repoPath, "commit", "-m", "B: main advance");
        var bSha = RevParse("HEAD");

        // Land back on feature so a rebase onto main is the natural next step.
        RunGit(_repoPath, "checkout", "feature");

        return (aSha, bSha, cSha);
    }

    private string RevParse(string rev)
    {
        var (code, stdout, stderr) = RunGitAllowFail(_repoPath, "rev-parse", rev);
        if (code != 0)
            throw new InvalidOperationException($"rev-parse {rev} failed: {stderr.Trim()}");
        return stdout.Trim();
    }

    [Fact]
    public async Task Rebase_LinearWithNoConflicts_ReplaysHeadOntoTarget()
    {
        var (_, bSha, cSha) = CreateLinearScenario();
        var svc = CreateService();

        var result = await svc.RebaseAsync(_repoPath, "main");

        result.Success.Should().BeTrue();
        result.HasConflicts.Should().BeFalse();

        // Feature's tip must now be a freshly-replayed commit (different SHA
        // than the original C) whose parent is B. The original C SHA stops
        // being reachable from HEAD's first parent.
        var newTip = RevParse("HEAD");
        newTip.Should().NotBe(cSha, "rebase should rewrite the commit");
        var firstParent = RevParse("HEAD^");
        firstParent.Should().Be(bSha, "the rewritten commit's parent must be main's tip");

        // Both files must coexist in the working tree.
        File.Exists(Path.Combine(_repoPath, "main.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_repoPath, "feature.txt")).Should().BeTrue();

        // No paused-rebase state should be left behind.
        Directory.Exists(Path.Combine(_repoPath, ".git", "rebase-merge")).Should().BeFalse();
        Directory.Exists(Path.Combine(_repoPath, ".git", "rebase-apply")).Should().BeFalse();
    }

    [Fact]
    public async Task Rebase_OntoUnknownBranch_Throws()
    {
        CreateLinearScenario();
        var svc = CreateService();

        var act = async () => await svc.RebaseAsync(_repoPath, "does-not-exist");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does-not-exist*");
    }

    [Fact]
    public async Task Rebase_WithConflict_ReportsConflictsAndPausesOnDisk()
    {
        // Both branches touch shared.txt at the same lines so the rebase pauses.
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "line 1\n");
        RunGit(_repoPath, "add", "shared.txt");
        RunGit(_repoPath, "commit", "-m", "A: shared base");

        RunGit(_repoPath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "feature override\n");
        RunGit(_repoPath, "commit", "-am", "feature: override");

        RunGit(_repoPath, "checkout", "main");
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "main override\n");
        RunGit(_repoPath, "commit", "-am", "main: override");

        RunGit(_repoPath, "checkout", "feature");

        var svc = CreateService();
        var result = await svc.RebaseAsync(_repoPath, "main");

        result.Success.Should().BeFalse();
        result.HasConflicts.Should().BeTrue();
        // Either rebase-merge (most modern git) or rebase-apply (older
        // backend) must exist while the rebase is paused.
        var paused = Directory.Exists(Path.Combine(_repoPath, ".git", "rebase-merge")) ||
                     Directory.Exists(Path.Combine(_repoPath, ".git", "rebase-apply"));
        paused.Should().BeTrue("a paused rebase must leave its state directory behind");

        // IsRebaseInProgressAsync must agree with the on-disk state.
        var inProgress = await svc.IsRebaseInProgressAsync(_repoPath);
        inProgress.Should().BeTrue();
    }

    [Fact]
    public async Task AbortRebase_RestoresHeadAndClearsState()
    {
        // Reuse the conflict scenario, then abort.
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "line 1\n");
        RunGit(_repoPath, "add", "shared.txt");
        RunGit(_repoPath, "commit", "-m", "A: shared base");

        RunGit(_repoPath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "feature override\n");
        RunGit(_repoPath, "commit", "-am", "feature: override");
        var featureTipBefore = RevParse("HEAD");

        RunGit(_repoPath, "checkout", "main");
        File.WriteAllText(Path.Combine(_repoPath, "shared.txt"), "main override\n");
        RunGit(_repoPath, "commit", "-am", "main: override");

        RunGit(_repoPath, "checkout", "feature");

        var svc = CreateService();
        var result = await svc.RebaseAsync(_repoPath, "main");
        result.HasConflicts.Should().BeTrue();

        await svc.AbortRebaseAsync(_repoPath);

        // After abort: no paused state, HEAD restored to feature's pre-rebase tip.
        Directory.Exists(Path.Combine(_repoPath, ".git", "rebase-merge")).Should().BeFalse();
        Directory.Exists(Path.Combine(_repoPath, ".git", "rebase-apply")).Should().BeFalse();
        RevParse("HEAD").Should().Be(featureTipBefore);
        (await svc.IsRebaseInProgressAsync(_repoPath)).Should().BeFalse();
    }

    [Fact]
    public async Task Rebase_WithAutosquash_CollapsesFixupCommit()
    {
        // Set up: A on main; feature has C, then a fixup! C commit. After
        // rebasing onto main with --autosquash, the fixup is folded into C
        // so the feature branch has exactly one commit on top of main.
        File.WriteAllText(Path.Combine(_repoPath, "a.txt"), "1\n");
        RunGit(_repoPath, "add", "a.txt");
        RunGit(_repoPath, "commit", "-m", "A");

        RunGit(_repoPath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, "f.txt"), "feature\n");
        RunGit(_repoPath, "add", "f.txt");
        RunGit(_repoPath, "commit", "-m", "C: feature");

        File.AppendAllText(Path.Combine(_repoPath, "f.txt"), "fixup line\n");
        RunGit(_repoPath, "commit", "-am", "fixup! C: feature");

        // Advance main so there's actually somewhere to rebase onto.
        RunGit(_repoPath, "checkout", "main");
        File.WriteAllText(Path.Combine(_repoPath, "m.txt"), "main\n");
        RunGit(_repoPath, "add", "m.txt");
        RunGit(_repoPath, "commit", "-m", "B: main");

        RunGit(_repoPath, "checkout", "feature");

        var svc = CreateService();
        var result = await svc.RebaseAsync(_repoPath, "main", autosquash: true);

        result.Success.Should().BeTrue();

        // Count feature-unique commits — should be 1 after autosquash.
        var (code, stdout, _) = RunGitAllowFail(_repoPath, "log", "main..HEAD", "--oneline");
        code.Should().Be(0);
        var lines = stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1, "autosquash should have collapsed the fixup! into its target");
    }
}
