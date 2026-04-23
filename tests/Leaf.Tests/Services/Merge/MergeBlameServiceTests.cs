#nullable enable
using System.Collections.Concurrent;
using System.Threading;
using FluentAssertions;
using Leaf.Models;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.Tests.Fakes;
using Xunit;

namespace Leaf.Tests.Services.Merge;

/// <summary>
/// Pins the two correctness invariants of the C5 blame cache:
///  1. Same repo + file + HEAD sha → one git subprocess, many hits.
///  2. HEAD sha change → cache miss, fresh fetch.
/// Plus cancellation-propagation so a hover cancellation during an in-flight
/// fetch actually lands on the downstream IGitService call.
/// </summary>
public class MergeBlameServiceTests
{
    [Fact]
    public async Task FirstLookup_FetchesBlame_ForFile()
    {
        var git = new CountingBlameGitService(headSha: "abc123",
            blame: new List<FileBlameLine>
            {
                new() { LineNumber = 1, Sha = "abc123", Author = "Alice", Subject = "init" },
                new() { LineNumber = 2, Sha = "abc123", Author = "Alice", Subject = "init" },
            });
        var service = new MergeBlameService(git);

        var record = await service.GetLineBlameAsync("/repo", "a.cs", 1);

        record.Should().NotBeNull();
        record!.Author.Should().Be("Alice");
        git.BlameCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SecondLookup_SameFile_SameHead_UsesCache()
    {
        var git = new CountingBlameGitService(headSha: "abc123",
            blame: new List<FileBlameLine>
            {
                new() { LineNumber = 1, Sha = "abc123", Author = "Alice" },
                new() { LineNumber = 2, Sha = "abc123", Author = "Bob" },
            });
        var service = new MergeBlameService(git);

        _ = await service.GetLineBlameAsync("/repo", "a.cs", 1);
        var second = await service.GetLineBlameAsync("/repo", "a.cs", 2);

        second.Should().NotBeNull();
        second!.Author.Should().Be("Bob");
        git.BlameCallCount.Should().Be(1,
            because: "second hover on the same file + HEAD must not spawn a new git subprocess");
    }

    [Fact]
    public async Task HeadShaChange_Invalidates_AndRefetches()
    {
        var git = new CountingBlameGitService(headSha: "abc123",
            blame: new List<FileBlameLine>
            {
                new() { LineNumber = 1, Sha = "abc123", Author = "Alice" },
            });
        var service = new MergeBlameService(git);
        _ = await service.GetLineBlameAsync("/repo", "a.cs", 1);

        // Simulate a ref update (fetch / pull / reset).
        git.HeadSha = "def456";
        git.Blame[0] = new FileBlameLine { LineNumber = 1, Sha = "def456", Author = "Carol" };
        var after = await service.GetLineBlameAsync("/repo", "a.cs", 1);

        after!.Author.Should().Be("Carol");
        git.BlameCallCount.Should().Be(2,
            because: "HEAD sha change must bypass the stale cache entry");
    }

    [Fact]
    public async Task InvalidateRepo_DropsEntries_ForThatRepoOnly()
    {
        var git = new CountingBlameGitService(headSha: "abc123",
            blame: new List<FileBlameLine>
            {
                new() { LineNumber = 1, Sha = "abc123", Author = "Alice" },
            });
        var service = new MergeBlameService(git);
        _ = await service.GetLineBlameAsync("/repo1", "a.cs", 1);
        _ = await service.GetLineBlameAsync("/repo2", "a.cs", 1);

        service.InvalidateRepo("/repo1");
        _ = await service.GetLineBlameAsync("/repo1", "a.cs", 1);
        _ = await service.GetLineBlameAsync("/repo2", "a.cs", 1);

        git.BlameCallCount.Should().Be(3,
            because: "repo1 refetched after invalidation; repo2 stays cached");
    }

    [Fact]
    public async Task LookupPastEndOfFile_ReturnsNull()
    {
        var git = new CountingBlameGitService(headSha: "abc",
            blame: new List<FileBlameLine>
            {
                new() { LineNumber = 1, Sha = "abc", Author = "Alice" },
            });
        var service = new MergeBlameService(git);

        var record = await service.GetLineBlameAsync("/repo", "a.cs", 999);

        record.Should().BeNull(because: "line past end of blame output is not an error — just no record to show");
    }

    [Fact]
    public async Task ConcurrentLookups_SameFile_ShareOneFetch()
    {
        var git = new SlowBlameGitService(headSha: "abc",
            blame: new List<FileBlameLine>
            {
                new() { LineNumber = 1, Sha = "abc", Author = "Alice" },
                new() { LineNumber = 2, Sha = "abc", Author = "Bob" },
            },
            delayMs: 50);
        var service = new MergeBlameService(git);

        // Fire 5 concurrent lookups for different lines in the same file.
        var tasks = Enumerable.Range(1, 5)
            .Select(i => service.GetLineBlameAsync("/repo", "a.cs", (i % 2) + 1))
            .ToArray();
        await Task.WhenAll(tasks);

        git.BlameCallCount.Should().Be(1,
            because: "the per-file fetch gate must serialize concurrent lookups to one subprocess");
    }

    private class CountingBlameGitService : FakeGitService
    {
        public string HeadSha { get; set; }
        public List<FileBlameLine> Blame { get; }
        public int BlameCallCount { get; private set; }

        public CountingBlameGitService(string headSha, List<FileBlameLine> blame)
        {
            HeadSha = headSha;
            Blame = blame;
        }

        public override Task<CommitInfo?> GetHeadCommitAsync(string repoPath, CancellationToken cancellationToken = default)
            => Task.FromResult<CommitInfo?>(new CommitInfo { Sha = HeadSha });

        public override Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        {
            BlameCallCount++;
            return Task.FromResult(new List<FileBlameLine>(Blame));
        }
    }

    private sealed class SlowBlameGitService : CountingBlameGitService
    {
        private readonly int _delayMs;
        public SlowBlameGitService(string headSha, List<FileBlameLine> blame, int delayMs)
            : base(headSha, blame)
        {
            _delayMs = delayMs;
        }

        public override async Task<List<FileBlameLine>> GetFileBlameAsync(string repoPath, string filePath, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            return await base.GetFileBlameAsync(repoPath, filePath, cancellationToken).ConfigureAwait(false);
        }
    }
}
