namespace Leaf.Models;

public sealed record CreatePullRequestRequest(string? SourceBranch = null, string? TargetBranch = null);
