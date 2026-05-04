using System.IO;
using FluentAssertions;
using Leaf.Core.InteractiveRebase;
using Leaf.Services;
using Xunit;

namespace Leaf.Tests.InteractiveRebase;

/// <summary>
/// Pins the contract of <see cref="RebaseHelperResolver"/> — the shared
/// path/env-var helper used by both the launch path
/// (<see cref="InteractiveRebaseService"/>) and the continue path
/// (<see cref="Leaf.Services.Git.Operations.RebaseOperations"/>).
/// </summary>
public class RebaseHelperResolverTests
{
    [Theory]
    [InlineData(@"C:\Users\Tim\AppData\Local\Programs\Leaf\Leaf.SequenceEditor.exe",
                "\"C:/Users/Tim/AppData/Local/Programs/Leaf/Leaf.SequenceEditor.exe\"")]
    [InlineData(@"C:\Program Files\Leaf\Leaf.SequenceEditor.exe",
                "\"C:/Program Files/Leaf/Leaf.SequenceEditor.exe\"")]
    public void ToShellEditorPath_ForwardSlashesAndQuotes(string input, string expected)
    {
        RebaseHelperResolver.ToShellEditorPath(input).Should().Be(expected);
    }

    [Fact]
    public void BuildLaunchEnvironment_AlwaysSetsSequenceEditor()
    {
        var env = RebaseHelperResolver.BuildLaunchEnvironment(
            helperPath: @"C:\bin\Leaf.SequenceEditor.exe",
            todoFile: @"C:\temp\rebase\git-rebase-todo",
            messagesDir: @"C:\temp\rebase\messages",
            cursorFile: @"C:\temp\rebase\cursor",
            overrideGitEditor: false);

        env.Should().ContainKey("GIT_SEQUENCE_EDITOR");
        env.Should().NotContainKey("GIT_EDITOR",
            because: "no reword/squash in the plan = no editor override");
        env[RebaseEditorRunner.TodoSourceEnv].Should().Be(@"C:\temp\rebase\git-rebase-todo");
        env[RebaseEditorRunner.MessagesDirEnv].Should().Be(@"C:\temp\rebase\messages");
        env[RebaseEditorRunner.MessageCursorEnv].Should().Be(@"C:\temp\rebase\cursor");
    }

    [Fact]
    public void BuildLaunchEnvironment_OverridesGitEditorWhenAsked()
    {
        var env = RebaseHelperResolver.BuildLaunchEnvironment(
            helperPath: @"C:\bin\Leaf.SequenceEditor.exe",
            todoFile: @"C:\temp\todo",
            messagesDir: @"C:\temp\msg",
            cursorFile: @"C:\temp\cur",
            overrideGitEditor: true);

        env.Should().ContainKey("GIT_EDITOR");
        env["GIT_EDITOR"].Should().Be(env["GIT_SEQUENCE_EDITOR"]);
    }

    [Fact]
    public void BuildContinuationEnvironment_NoMarker_ReturnsNull()
    {
        // Fresh empty git dir simulating a non-Leaf-driven rebase pause.
        var gitDir = Path.Combine(Path.GetTempPath(), "leaf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(gitDir, "rebase-merge"));
        try
        {
            var env = RebaseHelperResolver.BuildContinuationEnvironment(gitDir);
            env.Should().BeNull(
                because: "git rebase --continue must run without our editor override " +
                         "when the rebase wasn't started by Leaf");
        }
        finally
        {
            Directory.Delete(gitDir, recursive: true);
        }
    }

    [Fact]
    public void BuildContinuationEnvironment_MarkerPointsAtMissingDir_ReturnsNull()
    {
        var gitDir = Path.Combine(Path.GetTempPath(), "leaf-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(gitDir, "rebase-merge"));
        File.WriteAllText(
            RebaseHelperResolver.LeafTempMarkerPath(gitDir),
            @"C:\does\not\exist");
        try
        {
            // Marker present but its target dir is gone (user wiped %TEMP%
            // between paused rebase and continue). Falling back to a
            // non-Leaf continue is the safe behaviour — the previous
            // todo / cursor state is irrecoverable.
            var env = RebaseHelperResolver.BuildContinuationEnvironment(gitDir);
            env.Should().BeNull();
        }
        finally
        {
            Directory.Delete(gitDir, recursive: true);
        }
    }

    [Fact]
    public void LeafTempMarkerPath_LandsInsideRebaseMerge()
    {
        var path = RebaseHelperResolver.LeafTempMarkerPath(@"C:\repo\.git");
        path.Should().Be(@"C:\repo\.git\rebase-merge\leaf-rebase-temp");
    }
}
