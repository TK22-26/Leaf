using FluentAssertions;
using Leaf.Models;
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
    public void ParseStatus_OutOfSyncPrefix_MapsToOutOfSyncStatus()
    {
        const string output = "+1234567890123456789012345678901234567890 libs/foo (v1.2.3-4-gdeadbee)\n";

        var result = SubmoduleOperations.ParseSubmoduleStatusOutput(output, new Dictionary<string, SubmoduleOperations.ModuleConfigEntry>());

        result[0].Status.Should().Be(SubmoduleStatus.OutOfSync);
        result[0].IsDirty.Should().BeTrue();
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
}
