using Leaf.Services;

namespace Leaf.Tests.Fakes;

/// <summary>
/// Fake AI commit-message generator — returns a canned message without
/// talking to any provider. Tests that care about the prompt or provider
/// selection should swap for a richer fake.
/// </summary>
public class FakeAiCommitMessageService : IAiCommitMessageService
{
    public Task<(string? message, string? description, string? error)> GenerateCommitMessageAsync(
        string diffText, string? repoPath = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<(string?, string?, string?)>(("Test commit", "Test description", null));
    }
}
