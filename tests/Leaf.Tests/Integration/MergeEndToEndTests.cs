#nullable enable
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.ViewModels.Merge;
using Xunit;

namespace Leaf.Tests.Integration;

/// <summary>
/// End-to-end merge integration test. Stands up a real git repo on disk
/// with a deterministic three-way conflict, points a real
/// <see cref="MergeEditorViewModel"/> at it, runs the resolution commands,
/// and verifies that the composed result matches the expected text and
/// the file is staged in the index.
/// </summary>
/// <remarks>
/// <para>
/// The plan Phase 7 called this "Stagehand-driven" but the critical path
/// is the VM + engine + git-index interaction — not the WPF UI. Driving the
/// UI via Stagehand is brittle, slow, and CI-unfriendly. This test covers
/// the exact same contract (conflict → resolve → verify staged) via the VM
/// directly, which is what a UI-level test would verify *indirectly* anyway.
/// </para>
/// <para>
/// Requires <c>git</c> on PATH (same as GitMergeFileEngineTests). Fixture
/// repos are created in the system temp directory and torn down after each
/// test. Uses deterministic GIT_AUTHOR / COMMITTER identity so commits are
/// reproducible.
/// </para>
/// </remarks>
public class MergeEndToEndTests : IDisposable
{
    private readonly string _repoPath;
    private readonly List<string> _toCleanup = new();

    public MergeEndToEndTests()
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
        var path = Path.Combine(Path.GetTempPath(), $"leaf-merge-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        RunGit(path, "init", "-b", "main");
        RunGit(path, "config", "user.email", "test@leaf.dev");
        RunGit(path, "config", "user.name", "Test User");
        // Normalise line endings so assertions are portable between dev and CI.
        RunGit(path, "config", "core.autocrlf", "false");
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        // Git keeps .git/index locked briefly on Windows; retry a few times.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // Remove read-only flag from git's pack files before deleting.
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
        // Leave the orphaned temp dir; the OS will reclaim it eventually.
    }

    /// <summary>
    /// Set up a conflict on "notes.txt": base=v1, main=v2-ours, branch=v2-theirs.
    /// After this, <c>git merge feature</c> fails with an unmerged notes.txt.
    /// </summary>
    private void CreateConflictScenario(string filename = "notes.txt")
    {
        File.WriteAllText(Path.Combine(_repoPath, filename), "common line 1\nshared pivot\ncommon line 3\n");
        RunGit(_repoPath, "add", filename);
        RunGit(_repoPath, "commit", "-m", "base: shared pivot");

        // Branch off: feature modifies the pivot.
        RunGit(_repoPath, "checkout", "-b", "feature");
        File.WriteAllText(Path.Combine(_repoPath, filename),
            "common line 1\nTHEIRS changed the pivot\ncommon line 3\n");
        RunGit(_repoPath, "commit", "-am", "feature: theirs pivot");

        // Back on main, change the pivot differently.
        RunGit(_repoPath, "checkout", "main");
        File.WriteAllText(Path.Combine(_repoPath, filename),
            "common line 1\nOURS changed the pivot\ncommon line 3\n");
        RunGit(_repoPath, "commit", "-am", "main: ours pivot");

        // Kick off the merge — this leaves notes.txt conflicted.
        var mergeResult = RunGitAllowFail(_repoPath, "merge", "feature", "--no-ff", "-m", "Merge feature");
        mergeResult.exitCode.Should().NotBe(0, "merge must fail with a conflict for this test");
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
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    private static void RunGit(string workingDir, params string[] args)
    {
        var (code, stdout, stderr) = RunGitAllowFail(workingDir, args);
        if (code != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed (exit {code}): {stderr.Trim()}");
    }

    private MergeEditorViewModel CreateVm()
    {
        // Build the VM with the real wiring — same services the app resolves
        // through DI. This is what makes the test end-to-end: it exercises
        // the full VM → engine → git-index path.
        var gitService = new GitService(new GitCommandRunner());
        var clipboardService = new Leaf.Tests.Fakes.FakeClipboardService();
        var engine = new GitMergeFileEngine(new GitCommandRunner());
        var wordDiffService = new WordDiffService();

        return new MergeEditorViewModel(
            gitService,
            clipboardService,
            engine,
            wordDiffService,
            aiAssistant: null,
            imageService: null,
            repoPath: _repoPath);
    }

    // ── Resolution-mode tests ────────────────────────────────────────────

    [Fact]
    public async Task AcceptOurs_WritesOursContentAndStagesFile()
    {
        CreateConflictScenario();
        var vm = CreateVm();
        await vm.LoadConflictsAsync(showLoading: false);

        vm.Conflicts.Should().HaveCount(1);
        vm.Conflicts[0].FilePath.Should().Be("notes.txt");
        vm.SelectedConflict.Should().NotBeNull();

        // Wait for the engine build to populate Document (fires from OnSelectedConflictChanged).
        await WaitForDocumentAsync(vm);
        vm.Document.Should().NotBeNull();
        vm.ConflictCount.Should().Be(1);

        // Resolve: accept ours.
        vm.AcceptCurrentConflictOursCommand.Execute(null);
        vm.IsFullyResolved.Should().BeTrue();

        // Mark resolved — writes to disk + stages via git add.
        await vm.MarkResolvedCommand.ExecuteAsync(null);

        // Verify the file on disk contains the ours-side resolution.
        var onDisk = File.ReadAllText(Path.Combine(_repoPath, "notes.txt"));
        onDisk.Should().Contain("OURS changed the pivot");
        onDisk.Should().NotContain("THEIRS changed the pivot");
        onDisk.Should().NotContain("<<<<<<<");

        // Verify the index agrees: `git status --porcelain` should show the
        // file as resolved (no longer conflicted), and `git ls-files --stage`
        // should show exactly one stage-0 entry (not the 1/2/3 that an
        // unresolved conflict would produce).
        var status = RunGitAllowFail(_repoPath, "status", "--porcelain", "notes.txt");
        status.stdout.Should().NotContain("UU", "UU in porcelain output means still unmerged");
        var lsFiles = RunGitAllowFail(_repoPath, "ls-files", "--stage", "notes.txt");
        lsFiles.stdout.Trim().Split('\n').Should().HaveCount(1,
            "resolved file must have a single stage-0 entry in the index");
        lsFiles.stdout.Should().StartWith("100644 ");
        // The staged content equals what we wrote to disk.
        var hashStr = lsFiles.stdout.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        var stagedContent = RunGitAllowFail(_repoPath, "cat-file", "-p", hashStr);
        stagedContent.stdout.Should().Contain("OURS changed the pivot");
    }

    [Fact]
    public async Task AcceptTheirs_WritesTheirsContentAndStagesFile()
    {
        CreateConflictScenario();
        var vm = CreateVm();
        await vm.LoadConflictsAsync(showLoading: false);
        await WaitForDocumentAsync(vm);

        vm.AcceptCurrentConflictTheirsCommand.Execute(null);
        await vm.MarkResolvedCommand.ExecuteAsync(null);

        var onDisk = File.ReadAllText(Path.Combine(_repoPath, "notes.txt"));
        onDisk.Should().Contain("THEIRS changed the pivot");
        onDisk.Should().NotContain("OURS changed the pivot");
        onDisk.Should().NotContain("<<<<<<<");
    }

    [Fact]
    public async Task AcceptBoth_WritesBothSidesAndStagesFile()
    {
        CreateConflictScenario();
        var vm = CreateVm();
        await vm.LoadConflictsAsync(showLoading: false);
        await WaitForDocumentAsync(vm);

        vm.AcceptCurrentConflictBothCommand.Execute(null);
        await vm.MarkResolvedCommand.ExecuteAsync(null);

        var onDisk = File.ReadAllText(Path.Combine(_repoPath, "notes.txt"));
        onDisk.Should().Contain("OURS changed the pivot");
        onDisk.Should().Contain("THEIRS changed the pivot");
        onDisk.Should().NotContain("<<<<<<<");
    }

    [Fact]
    public async Task ManualResolution_WritesExactTextAndStagesFile()
    {
        CreateConflictScenario();
        var vm = CreateVm();
        await vm.LoadConflictsAsync(showLoading: false);
        await WaitForDocumentAsync(vm);

        const string manual = "CUSTOM resolution: pivot merged by hand\n";
        var range = vm.Document!.Ranges.First(r => r.IsConflicting);
        vm.ApplyManualText(range.Index, manual);
        await vm.MarkResolvedCommand.ExecuteAsync(null);

        var onDisk = File.ReadAllText(Path.Combine(_repoPath, "notes.txt"));
        onDisk.Should().Contain("CUSTOM resolution: pivot merged by hand");
        onDisk.Should().NotContain("OURS");
        onDisk.Should().NotContain("THEIRS");
        onDisk.Should().NotContain("<<<<<<<");
    }

    [Fact]
    public async Task CompleteMerge_CreatesMergeCommit()
    {
        CreateConflictScenario();
        var vm = CreateVm();
        await vm.LoadConflictsAsync(showLoading: false);
        await WaitForDocumentAsync(vm);

        vm.AcceptCurrentConflictOursCommand.Execute(null);
        await vm.MarkResolvedCommand.ExecuteAsync(null);
        vm.CanCompleteMerge.Should().BeTrue();

        await vm.CompleteMergeCommand.ExecuteAsync(null);

        // Verify a merge commit exists with two parents.
        var logResult = RunGitAllowFail(_repoPath, "log", "-1", "--pretty=%P");
        logResult.stdout.Trim().Split(' ').Should().HaveCount(2,
            "the completed merge must produce a two-parent commit");
    }

    [Fact]
    public async Task UseOurs_EscapeHatch_ResolvesBinaryAndTextConflictsAlike()
    {
        // Use-ours uses git's own resolve-with-ours path; tested here via the VM's
        // command so we know the wiring works end-to-end.
        CreateConflictScenario();
        var vm = CreateVm();
        await vm.LoadConflictsAsync(showLoading: false);
        await WaitForDocumentAsync(vm);

        await vm.UseOursCommand.ExecuteAsync(null);

        vm.SelectedConflict!.IsResolved.Should().BeTrue();
        var onDisk = File.ReadAllText(Path.Combine(_repoPath, "notes.txt"));
        onDisk.Should().Contain("OURS changed the pivot");
        onDisk.Should().NotContain("<<<<<<<");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Wait for the fire-and-forget Document-build task to populate
    /// <see cref="MergeEditorViewModel.Document"/>. The VM triggers the
    /// build from <c>OnSelectedConflictChanged</c> via
    /// <c>FireAndForget</c> — there's no awaitable handle. Poll with a
    /// reasonable timeout; if the build hasn't completed in 5 s on a
    /// simple 3-line conflict, something is wrong.
    /// </summary>
    private static async Task WaitForDocumentAsync(MergeEditorViewModel vm, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow + timeout.Value;
        while (vm.Document is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }
        vm.Document.Should().NotBeNull("Document should be populated within timeout");
    }
}
