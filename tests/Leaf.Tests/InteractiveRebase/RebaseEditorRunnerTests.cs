using FluentAssertions;
using Leaf.Core.InteractiveRebase;
using Xunit;

namespace Leaf.Tests.InteractiveRebase;

/// <summary>
/// Direct unit tests for the helper-exe logic. The Program.cs in
/// Leaf.SequenceEditor is a thin wrapper around <see cref="RebaseEditorRunner"/>
/// so these tests cover every code path the helper can take in production
/// without spawning a process.
/// </summary>
public class RebaseEditorRunnerTests
{
    [Fact]
    public void Run_NoArgs_FailsWithMissingArgument()
    {
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();

        var outcome = RebaseEditorRunner.Run([], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.MissingArgument);
        diagnostic.Should().Contain("no file path argument");
    }

    [Fact]
    public void Run_TodoFile_CopiesSourceContents()
    {
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        const string sourcePath = @"C:\temp\plan\git-rebase-todo";
        const string targetPath = @"C:\repo\.git\rebase-merge\git-rebase-todo";
        fs.SetFile(sourcePath, "pick abc123 first\npick def456 second\n");
        env.Set(RebaseEditorRunner.TodoSourceEnv, sourcePath);

        var outcome = RebaseEditorRunner.Run([targetPath], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.Success);
        diagnostic.Should().BeEmpty();
        fs.ReadAllText(targetPath).Should().Be("pick abc123 first\npick def456 second\n");
    }

    [Fact]
    public void Run_TodoFile_WithoutEnv_LeavesGitDefaultUntouched()
    {
        // No LEAF_REBASE_TODO_FILE means Leaf isn't driving — the rebase
        // was started outside our service. Helper exits 0 so git uses
        // whatever it already wrote to the file.
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        fs.SetFile(@"C:\repo\.git\rebase-merge\git-rebase-todo", "pick abc original\n");

        var outcome = RebaseEditorRunner.Run(
            [@"C:\repo\.git\rebase-merge\git-rebase-todo"], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.Success);
        diagnostic.Should().BeEmpty();
        fs.ReadAllText(@"C:\repo\.git\rebase-merge\git-rebase-todo")
            .Should().Be("pick abc original\n", because: "the helper must not touch git's default content");
    }

    [Fact]
    public void Run_TodoFile_SourceMissing_FailsLoudly()
    {
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        env.Set(RebaseEditorRunner.TodoSourceEnv, @"C:\temp\does-not-exist");

        var outcome = RebaseEditorRunner.Run(
            [@"C:\repo\.git\rebase-merge\git-rebase-todo"], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.TodoSourceMissing);
        diagnostic.Should().Contain("does-not-exist");
    }

    [Fact]
    public void Run_CommitMessage_PullsFromQueueInOrder()
    {
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        const string dir = @"C:\temp\messages";
        const string cursor = @"C:\temp\cursor";
        fs.SetFile(System.IO.Path.Combine(dir, "0001.msg"), "first reword");
        fs.SetFile(System.IO.Path.Combine(dir, "0002.msg"), "second reword");
        fs.SetFile(cursor, "0");
        env.Set(RebaseEditorRunner.MessagesDirEnv, dir);
        env.Set(RebaseEditorRunner.MessageCursorEnv, cursor);

        var first = RebaseEditorRunner.Run(
            [@"C:\repo\.git\COMMIT_EDITMSG"], fs, env, out _);
        first.Should().Be(RebaseEditorRunner.Outcome.Success);
        fs.ReadAllText(@"C:\repo\.git\COMMIT_EDITMSG").Should().Be("first reword");
        fs.ReadAllText(cursor).Should().Be("1");

        var second = RebaseEditorRunner.Run(
            [@"C:\repo\.git\COMMIT_EDITMSG"], fs, env, out _);
        second.Should().Be(RebaseEditorRunner.Outcome.Success);
        fs.ReadAllText(@"C:\repo\.git\COMMIT_EDITMSG").Should().Be("second reword");
        fs.ReadAllText(cursor).Should().Be("2");
    }

    [Fact]
    public void Run_CommitMessage_QueueExhausted_FailsLoudly()
    {
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        const string dir = @"C:\temp\messages";
        const string cursor = @"C:\temp\cursor";
        fs.SetFile(System.IO.Path.Combine(dir, "0001.msg"), "only one queued");
        fs.SetFile(cursor, "1"); // already consumed
        env.Set(RebaseEditorRunner.MessagesDirEnv, dir);
        env.Set(RebaseEditorRunner.MessageCursorEnv, cursor);

        var outcome = RebaseEditorRunner.Run(
            [@"C:\repo\.git\COMMIT_EDITMSG"], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.CursorOutOfRange);
        diagnostic.Should().Contain("0002.msg");
    }

    [Fact]
    public void Run_CommitMessage_MessagesDirMissing_FailsLoudly()
    {
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        env.Set(RebaseEditorRunner.MessagesDirEnv, @"C:\temp\nope");
        env.Set(RebaseEditorRunner.MessageCursorEnv, @"C:\temp\cursor");

        var outcome = RebaseEditorRunner.Run(
            [@"C:\repo\.git\COMMIT_EDITMSG"], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.MessagesDirMissing);
        diagnostic.Should().Contain(@"C:\temp\nope");
    }

    [Fact]
    public void Run_UnrecognisedFile_LeavesUntouched()
    {
        // Git can call the editor for unrelated reasons (a hook firing
        // during exec, for instance). Out of scope = exit 0, no edit.
        var fs = new InMemoryFileSystem();
        var env = new InMemoryEnvironment();
        env.Set(RebaseEditorRunner.TodoSourceEnv, @"C:\temp\todo");
        fs.SetFile(@"C:\temp\todo", "should-not-be-used");
        fs.SetFile(@"C:\repo\.git\TAG_EDITMSG", "original tag message");

        var outcome = RebaseEditorRunner.Run(
            [@"C:\repo\.git\TAG_EDITMSG"], fs, env, out var diagnostic);

        outcome.Should().Be(RebaseEditorRunner.Outcome.Success);
        diagnostic.Should().BeEmpty();
        fs.ReadAllText(@"C:\repo\.git\TAG_EDITMSG").Should().Be("original tag message");
    }

    private sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public void SetFile(string path, string contents) => _files[path] = contents;

        public bool FileExists(string path) => _files.ContainsKey(path);

        public bool DirectoryExists(string path) =>
            _files.Keys.Any(p => p.StartsWith(path + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

        public string ReadAllText(string path) => _files[path];

        public void WriteAllText(string path, string contents) => _files[path] = contents;

        public void AppendAllText(string path, string contents)
        {
            _files[path] = (_files.TryGetValue(path, out var existing) ? existing : string.Empty) + contents;
        }
    }

    private sealed class InMemoryEnvironment : IEnvironment
    {
        private readonly Dictionary<string, string> _values =
            new(StringComparer.OrdinalIgnoreCase);

        public void Set(string name, string value) => _values[name] = value;

        public string? GetVariable(string name) =>
            _values.TryGetValue(name, out var v) ? v : null;
    }
}
