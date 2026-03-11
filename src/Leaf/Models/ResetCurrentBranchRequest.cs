using Leaf.Services;

namespace Leaf.Models;

public record ResetCurrentBranchRequest(CommitInfo Commit, GitResetMode Mode);
