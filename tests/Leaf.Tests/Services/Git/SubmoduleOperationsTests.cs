using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Git.Core;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services.Git;

/// <summary>
/// Unit tests for <see cref="SubmoduleOperations"/> parsing. The live
/// CLI interaction is covered by the integration smoke tests; these
/// focus on the string parsers so the ten-way status prefix / path
/// layout / config-merge logic is regression-safe.
/// </summary>
public class SubmoduleOperationsTests
{
    // ---- ParseIndexGitlinks (orphaned-gitlink fallback primitive) --------

    [Fact]
    public void ParseIndexGitlinks_Empty_ReturnsEmpty()
    {
        SubmoduleOperations.ParseIndexGitlinks("").Should().BeEmpty();
    }

    [Fact]
    public void ParseIndexGitlinks_PicksGitlinksSkipsRegularFiles()
    {
        // `git ls-files --stage` mixes blobs (100644), executables (100755)
        // and gitlinks (160000). Only the gitlinks are submodule pointers.
        var output = string.Join('\n',
            "100644 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa 0\treadme.txt",
            "160000 bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 0\tlibs/foo",
            "100755 cccccccccccccccccccccccccccccccccccccccc 0\tbuild.sh",
            "160000 dddddddddddddddddddddddddddddddddddddddd 0\t.claude/worktrees/agent-ac88db97");

        var map = SubmoduleOperations.ParseIndexGitlinks(output);

        map.Should().HaveCount(2);
        map["libs/foo"].Should().Be("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        map[".claude/worktrees/agent-ac88db97"].Should().Be("dddddddddddddddddddddddddddddddddddddddd");
        map.Should().NotContainKey("readme.txt");
        map.Should().NotContainKey("build.sh");
    }

    [Fact]
    public void ParseIndexGitlinks_NormalizesBackslashesToForwardSlashes()
    {
        var output = "160000 1111111111111111111111111111111111111111 0\tvendor\\bar";

        var map = SubmoduleOperations.ParseIndexGitlinks(output);

        map.Should().ContainKey("vendor/bar");
    }

    [Fact]
    public void ParseIndexGitlinks_ConflictedGitlink_FirstStageWins()
    {
        // A conflicted gitlink surfaces as multiple stage entries; the
        // sidebar only needs one coarse recorded SHA and this only runs on
        // the already-degraded path, so first-seen is sufficient.
        var output = string.Join('\n',
            "160000 2222222222222222222222222222222222222222 1\tlibs/foo",
            "160000 3333333333333333333333333333333333333333 2\tlibs/foo",
            "160000 4444444444444444444444444444444444444444 3\tlibs/foo");

        var map = SubmoduleOperations.ParseIndexGitlinks(output);

        map.Should().ContainSingle();
        map["libs/foo"].Should().Be("2222222222222222222222222222222222222222");
    }

    [Fact]
    public void ParseIndexGitlinks_RejectsPathTraversal()
    {
        var output = "160000 5555555555555555555555555555555555555555 0\t../escape";

        SubmoduleOperations.ParseIndexGitlinks(output).Should().BeEmpty();
    }

    // ---- ParseSubmoduleStatusOutput --------------------------------------

    [Fact]
    public void ParseStatus_Empty_ReturnsEmpty()
    {
        var result = SubmoduleOperations.ParseSubmoduleStatusOutput("", new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseStatus_UpToDate_ParsesShaPathDescribe()
    {
        const string output = " abcdef0123456789abcdef0123456789abcdef01 libs/foo (heads/main)\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result.Should().HaveCount(1);
        var sm = result[0];
        sm.Path.Should().Be("libs/foo");
        sm.RecordedSha.Should().Be("abcdef0123456789abcdef0123456789abcdef01");
        sm.WorkingSha.Should().Be("abcdef0123456789abcdef0123456789abcdef01");
        sm.Describe.Should().Be("heads/main");
        sm.Status.Should().Be(SubmoduleStatus.UpToDate);
        sm.IsInitialized.Should().BeTrue();
        sm.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ParseStatus_UninitializedPrefix_SetsStatusAndNullWorkingSha()
    {
        const string output = "-0000000000000000000000000000000000000000 vendor/bar\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result.Should().HaveCount(1);
        result[0].Status.Should().Be(SubmoduleStatus.Uninitialized);
        result[0].WorkingSha.Should().BeNull();
        result[0].RecordedSha.Should().Be("0000000000000000000000000000000000000000");
        result[0].IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void ParseStatus_OutOfSyncPrefix_ShaIsWorkingNotRecorded()
    {
        // For `+` prefix `git submodule status` prints the *working*
        // commit; the recorded SHA is what the parent tree stores and
        // only comes from `status --cached`. When no cached map is
        // provided, RecordedSha must be empty rather than a duplicate
        // of the working SHA — that was the pre-audit bug.
        const string output = "+1234567890123456789012345678901234567890 libs/foo (v1.2.3-4-gdeadbee)\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result[0].Status.Should().Be(SubmoduleStatus.OutOfSync);
        result[0].IsDirty.Should().BeTrue();
        result[0].WorkingSha.Should().Be("1234567890123456789012345678901234567890");
        result[0].RecordedSha.Should().BeEmpty();
    }

    [Fact]
    public void ParseStatus_OutOfSync_WithCachedMap_UsesMapForRecordedSha()
    {
        const string output = "+1111111111111111111111111111111111111111 libs/foo (v1)\n";
        var cached = new Dictionary<string, string>
        {
            ["libs/foo"] = "2222222222222222222222222222222222222222",
        };

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(
            output,
            new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>(),
            cached);

        result[0].WorkingSha.Should().Be("1111111111111111111111111111111111111111");
        result[0].RecordedSha.Should().Be("2222222222222222222222222222222222222222");
    }

    [Fact]
    public void ParseStatus_UpToDate_WithCachedMap_BothShasMatch()
    {
        // When working == recorded, both sources agree — the cached
        // map takes precedence but produces the same value.
        const string output = " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa libs/foo (heads/main)\n";
        var cached = new Dictionary<string, string>
        {
            ["libs/foo"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        };

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(
            output,
            new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>(),
            cached);

        result[0].WorkingSha.Should().Be("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        result[0].RecordedSha.Should().Be("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    }

    [Fact]
    public void ParseStatus_UninitializedWithoutCachedMap_UsesLineShaAsRecorded()
    {
        // For `-` the line's SHA IS the recorded one (no working tree
        // to report) — the fallback path must preserve that meaning.
        const string output = "-abcabcabcabcabcabcabcabcabcabcabcabcabca libs/foo\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(
            output,
            new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result[0].Status.Should().Be(SubmoduleStatus.Uninitialized);
        result[0].WorkingSha.Should().BeNull();
        result[0].RecordedSha.Should().Be("abcabcabcabcabcabcabcabcabcabcabcabcabca");
    }

    [Fact]
    public void ParseStatus_UnknownPrefix_FallsThroughToUpToDate()
    {
        // Defensive: a new git version introducing, say, 'X' should
        // still produce a list entry rather than crash the sidebar.
        // The log warning (not asserted here) is the signal to
        // investigate.
        const string output = "X1111111111111111111111111111111111111111 libs/foo\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(
            output,
            new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result.Should().HaveCount(1);
        result[0].Status.Should().Be(SubmoduleStatus.UpToDate);
    }

    // ---- ParseRecordedShaByPath ------------------------------------------

    [Fact]
    public void ParseRecordedShaByPath_BuildsPathToShaMap()
    {
        const string cachedOutput =
            " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa libs/a\n" +
            " bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb libs/b (heads/main)\n";

        var result = SubmoduleOperations.ParseRecordedShaByPath(cachedOutput);

        result.Should().HaveCount(2);
        result["libs/a"].Should().Be("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        result["libs/b"].Should().Be("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    }

    [Fact]
    public void ParseStatus_ConflictedPrefix_MapsToConflicted()
    {
        const string output = "Ufedcba9876543210fedcba9876543210fedcba98 libs/foo\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result[0].Status.Should().Be(SubmoduleStatus.Conflicted);
        result[0].IsDirty.Should().BeTrue();
    }

    [Fact]
    public void ParseStatus_MultipleEntries_AllParsed()
    {
        const string output =
            " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa libs/a (heads/main)\n" +
            "-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb libs/b\n" +
            "+cccccccccccccccccccccccccccccccccccccccc vendor/c (v2)\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result.Should().HaveCount(3);
        result.Select(s => s.Path).Should().Equal("libs/a", "libs/b", "vendor/c");
        result.Select(s => s.Status).Should().Equal(
            SubmoduleStatus.UpToDate, SubmoduleStatus.Uninitialized, SubmoduleStatus.OutOfSync);
    }

    [Fact]
    public void ParseStatus_BackslashesInPath_NormalizedToForwardSlashes()
    {
        // Windows shell can surface paths with backslashes even from git;
        // normalise to git's native forward-slash form.
        const string output = " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa libs\\foo\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result[0].Path.Should().Be("libs/foo");
    }

    [Fact]
    public void ParseStatus_MatchesConfigByPath_AppliesNameUrlBranch()
    {
        const string output = " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa libs/foo (heads/main)\n";
        var cfg = new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>
        {
            // Name intentionally different from path to prove the join is on path.
            ["foo-alias"] = new SubmoduleOperations.ModuleConfigEntry
            {
                Name = "foo-alias",
                Path = "libs/foo",
                Url = "https://example.com/foo.git",
                Branch = "main",
            },
        };

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, cfg);

        result[0].Name.Should().Be("foo-alias");
        result[0].Url.Should().Be("https://example.com/foo.git");
        result[0].Branch.Should().Be("main");
    }

    [Fact]
    public void ParseStatus_UnconfiguredEntry_FallsBackToPathAsName()
    {
        // Status output contains a path that isn't in .gitmodules — e.g.
        // a stale entry. We surface what git told us rather than throw.
        const string output = " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa orphan/entry\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result[0].Name.Should().Be("orphan/entry");
        result[0].Url.Should().BeEmpty();
        result[0].Branch.Should().BeNull();
    }

    // ---- ParseGitmodulesConfig -------------------------------------------

    [Fact]
    public void ParseConfig_Empty_ReturnsEmpty()
    {
        SubmoduleOperations.ParseGitmodulesConfig("").Should().BeEmpty();
    }

    [Fact]
    public void ParseConfig_GroupsLinesByName()
    {
        const string output =
            "submodule.libs/foo.path=libs/foo\n" +
            "submodule.libs/foo.url=https://example.com/foo.git\n" +
            "submodule.libs/bar.path=libs/bar\n" +
            "submodule.libs/bar.url=https://example.com/bar.git\n" +
            "submodule.libs/bar.branch=develop\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().ContainKey("libs/foo");
        result["libs/foo"].Url.Should().Be("https://example.com/foo.git");
        result["libs/foo"].Branch.Should().BeNull();

        result.Should().ContainKey("libs/bar");
        result["libs/bar"].Branch.Should().Be("develop");
    }

    [Fact]
    public void ParseConfig_NameContainsDot_LastDotIsFieldSeparator()
    {
        // Section keys in gitconfig are delimited by the LAST dot; a
        // name like `libs.v2` is legal and must not be mis-split.
        const string output =
            "submodule.libs.v2.path=libs/v2\n" +
            "submodule.libs.v2.url=https://example.com/v2.git\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().ContainKey("libs.v2");
        result["libs.v2"].Path.Should().Be("libs/v2");
        result["libs.v2"].Url.Should().Be("https://example.com/v2.git");
    }

    [Fact]
    public void ParseConfig_EntryMissingPath_Dropped()
    {
        // Without a path we can't join to submodule status output, so
        // the entry is effectively invisible to the listing logic.
        const string output =
            "submodule.ghost.url=https://example.com/ghost.git\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().NotContainKey("ghost");
    }

    [Fact]
    public void ParseConfig_IgnoresNonSubmoduleKeys()
    {
        const string output =
            "core.autocrlf=true\n" +
            "submodule.libs/foo.path=libs/foo\n" +
            "submodule.libs/foo.url=x\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().HaveCount(1);
        result.Should().ContainKey("libs/foo");
    }

    // ---- Security: path-traversal / rooted-path rejection ----------------
    //
    // A hostile `.gitmodules` could try to send RemoveAsync's cache
    // delete at arbitrary directories (git CVE-2018-11235 class). The
    // parser must drop these entries instead of passing them along.

    [Fact]
    public void ParseConfig_NameWithParentTraversal_Dropped()
    {
        const string output =
            "submodule...path=evil\n" +
            "submodule...url=https://evil.example.com\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().NotContainKey("..");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseConfig_NameContainingDotDotSegment_Dropped()
    {
        const string output =
            "submodule.libs/../etc.path=evil\n" +
            "submodule.libs/../etc.url=https://evil.example.com\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseConfig_PathWithParentTraversal_Dropped()
    {
        const string output =
            "submodule.libs/foo.path=../outside\n" +
            "submodule.libs/foo.url=x\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        // Path rejected → entry purged in the post-pass.
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseConfig_RootedPathValue_Dropped()
    {
        const string output =
            "submodule.libs/foo.path=C:/Windows/Temp/evil\n" +
            "submodule.libs/foo.url=x\n";

        var result = SubmoduleOperations.ParseGitmodulesConfig(output);

        result.Should().BeEmpty();
    }

    [Fact]
    public void IsSafeRelativeComponent_HandlesKnownShapes()
    {
        // Safe shapes the parser should accept.
        SubmoduleOperations.IsSafeRelativeComponent("libs/foo").Should().BeTrue();
        SubmoduleOperations.IsSafeRelativeComponent("vendor/nested/thing").Should().BeTrue();
        SubmoduleOperations.IsSafeRelativeComponent("foo-bar_baz.qux").Should().BeTrue();

        // Unsafe shapes the parser must drop.
        SubmoduleOperations.IsSafeRelativeComponent("").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent("..").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent("a/..").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent("../outside").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent(".").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent("/absolute/posix").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent("C:/absolute/windows").Should().BeFalse();
        SubmoduleOperations.IsSafeRelativeComponent("a\\b").Should().BeFalse();
    }

    [Fact]
    public void ParseStatus_UnsafePathFromGit_Skipped()
    {
        // Defense in depth: if a rogue git build emits a traversal path,
        // the downstream parser should still drop it rather than let it
        // reach filesystem-touching code.
        const string output = " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa ../outside\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result.Should().BeEmpty();
    }

    // ---- CLI argv construction (thin wrappers) ---------------------------
    //
    // These catch regressions in the exact git CLI shape each mutation
    // emits — the whole point of wrapping git in code is to keep this
    // invariant. Uses a recording runner; no real git process is spawned.

    [Fact]
    public async Task InitAndUpdateAsync_SinglePath_EmitsInitUpdateWithSeparator()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.InitAndUpdateAsync("/repo", ["libs/foo"], recursive: false);

        runner.LastArgs.Should().Equal("submodule", "update", "--init", "--", "libs/foo");
    }

    [Fact]
    public async Task InitAndUpdateAsync_NoPaths_NoSeparator()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.InitAndUpdateAsync("/repo", [], recursive: false);

        runner.LastArgs.Should().Equal("submodule", "update", "--init");
    }

    [Fact]
    public async Task InitAndUpdateAsync_Recursive_AddsFlagBeforeSeparator()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.InitAndUpdateAsync("/repo", ["libs/foo", "vendor/bar"], recursive: true);

        runner.LastArgs.Should().Equal(
            "submodule", "update", "--init", "--recursive", "--", "libs/foo", "vendor/bar");
    }

    [Fact]
    public async Task SyncAsync_WithPathsAndRecursive_EmitsExpectedArgv()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.SyncAsync("/repo", ["libs/foo"], recursive: true);

        runner.LastArgs.Should().Equal("submodule", "sync", "--recursive", "--", "libs/foo");
    }

    [Fact]
    public async Task DeinitAsync_ForceFlag_EmitsForceAndSeparator()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.DeinitAsync("/repo", "libs/foo", force: true);

        runner.LastArgs.Should().Equal("submodule", "deinit", "--force", "--", "libs/foo");
    }

    [Fact]
    public async Task DeinitAsync_NoForce_OmitsForceFlag()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.DeinitAsync("/repo", "libs/foo", force: false);

        runner.LastArgs.Should().Equal("submodule", "deinit", "--", "libs/foo");
    }

    [Fact]
    public async Task AddAsync_WithBranch_EmitsBranchFlag()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.AddAsync("/repo", "https://example.com/repo.git", "libs/foo", "main");

        runner.LastArgs.Should().Equal(
            "submodule", "add", "-b", "main", "--", "https://example.com/repo.git", "libs/foo");
    }

    [Fact]
    public async Task AddAsync_NoBranch_OmitsBranchFlag()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.AddAsync("/repo", "https://example.com/repo.git", "libs/foo", branch: null);

        runner.LastArgs.Should().Equal(
            "submodule", "add", "--", "https://example.com/repo.git", "libs/foo");
    }

    [Fact]
    public async Task AddAsync_EmptyUrl_ThrowsBeforeInvokingRunner()
    {
        var runner = new RecordingRunner(_ => throw new InvalidOperationException("should not run"));
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.Invoking(o => o.AddAsync("/repo", "   ", "libs/foo", null))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateToRemoteAsync_EmitsRemoteFlag()
    {
        var runner = new RecordingRunner(_ => Ok());
        var ops = new SubmoduleOperations(new StubContext(runner));

        await ops.UpdateToRemoteAsync("/repo", "libs/foo");

        runner.LastArgs.Should().Equal("submodule", "update", "--remote", "--", "libs/foo");
    }

    [Fact]
    public async Task RemoveAsync_InitializedSubmodule_InvokesDeinitThenRmInOrder()
    {
        // Multi-step remove is the documented risk point — this test
        // pins the exact sequence: deinit first, then `git rm`. The
        // cache-dir delete happens between them but doesn't go through
        // the CommandRunner, so it doesn't appear in the recorded argv.
        var calls = new List<IReadOnlyList<string>>();
        var runner = new RecordingRunner(args =>
        {
            calls.Add(args.ToList());
            return Ok();
        });
        var ops = new SubmoduleOperations(new StubContext(runner));

        var submodule = new SubmoduleInfo
        {
            Name = "libs/foo",
            Path = "libs/foo",
            Url = "https://example.com/foo.git",
            RecordedSha = "a".PadRight(40, 'a'),
            Status = SubmoduleStatus.UpToDate,
        };

        await ops.RemoveAsync("/repo", submodule);

        calls.Should().HaveCount(2);
        calls[0].Should().Equal("submodule", "deinit", "--force", "--", "libs/foo");
        calls[1].Should().Equal("rm", "-f", "--", "libs/foo");
    }

    [Fact]
    public async Task RemoveAsync_UninitializedSubmodule_SkipsDeinitStep()
    {
        var calls = new List<IReadOnlyList<string>>();
        var runner = new RecordingRunner(args =>
        {
            calls.Add(args.ToList());
            return Ok();
        });
        var ops = new SubmoduleOperations(new StubContext(runner));

        var submodule = new SubmoduleInfo
        {
            Name = "libs/foo",
            Path = "libs/foo",
            Url = "https://example.com/foo.git",
            RecordedSha = "a".PadRight(40, 'a'),
            Status = SubmoduleStatus.Uninitialized,
        };

        await ops.RemoveAsync("/repo", submodule);

        calls.Should().ContainSingle();
        calls[0].Should().Equal("rm", "-f", "--", "libs/foo");
    }

    [Fact]
    public async Task RemoveAsync_DeinitFails_ThrowsBeforeGitRm()
    {
        // Regression guard: the pre-audit version log-and-continue'd
        // on deinit failure, leaving local config pointing at a cache
        // dir we were about to delete. Now: fail fast.
        var calls = new List<IReadOnlyList<string>>();
        var runner = new RecordingRunner(args =>
        {
            calls.Add(args.ToList());
            if (args.Count > 1 && args[1] == "deinit")
            {
                return new GitCommandResult(1, string.Empty, "deinit failed", Success: false);
            }
            return Ok();
        });
        var ops = new SubmoduleOperations(new StubContext(runner));

        var submodule = new SubmoduleInfo
        {
            Name = "libs/foo",
            Path = "libs/foo",
            Url = "",
            RecordedSha = "a".PadRight(40, 'a'),
            Status = SubmoduleStatus.UpToDate,
        };

        await ops.Invoking(o => o.RemoveAsync("/repo", submodule))
            .Should().ThrowAsync<InvalidOperationException>();
        calls.Should().ContainSingle("git rm must not run after a failed deinit");
    }

    private static GitCommandResult Ok() => new(0, string.Empty, string.Empty, Success: true);

    // Records the arguments of each invocation so tests can assert on
    // the exact CLI shape. Shared with ConfigOperationsTests in spirit;
    // duplicated here to keep the submodule test file self-contained.
    private sealed class RecordingRunner : IGitCommandRunner
    {
        private readonly Func<IReadOnlyList<string>, GitCommandResult> _respond;

        public RecordingRunner(Func<IReadOnlyList<string>, GitCommandResult> respond)
        {
            _respond = respond;
        }

        public IReadOnlyList<string> LastArgs { get; private set; } = [];

        public event EventHandler<GitCommandEventArgs>? CommandExecuted;

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            string? input = null,
            string? credentialKey = null,
            IReadOnlyDictionary<string, string>? extraEnvironment = null,
            CancellationToken cancellationToken = default)
        {
            LastArgs = arguments;
            var result = _respond(arguments);
            CommandExecuted?.Invoke(this, new GitCommandEventArgs(
                workingDirectory, string.Join(" ", arguments),
                result.ExitCode, result.StandardOutput, result.StandardError));
            return Task.FromResult(result);
        }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            GitCommand command,
            CancellationToken cancellationToken = default)
        {
            return RunAsync(workingDirectory, command.ToArguments(), cancellationToken: cancellationToken);
        }
    }

    // Stub context that exposes only the CommandRunner — the operations
    // under test don't touch the parser or mapper.
    private sealed class StubContext : IGitOperationContext
    {
        public StubContext(IGitCommandRunner runner) { CommandRunner = runner; }

        public IGitCommandRunner CommandRunner { get; }
        public IGitOutputParser OutputParser => throw new NotImplementedException();
        public IGitErrorMapper ErrorMapper => throw new NotImplementedException();
    }
}
