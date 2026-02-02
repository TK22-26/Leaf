using Leaf.Models;

namespace Leaf.Services;

/// <summary>
/// Service for managing git branches.
/// Delegates to GitService for backward compatibility.
/// </summary>
public class BranchService : IBranchService
{
    private readonly IGitService _gitService;
    private readonly IRepositoryEventHub _eventHub;

    public BranchService(IGitService gitService, IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(IRepositorySession session)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        return await _gitService.GetBranchesAsync(session.RepositoryPath);
    }

    /// <inheritdoc />
    public async Task CreateBranchAsync(IRepositorySession session, string branchName, bool checkout = true)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CreateBranchAsync(session.RepositoryPath, branchName, checkout);
        _eventHub.NotifyBranchesChanged();
        if (checkout)
        {
            _eventHub.NotifyWorkingDirectoryChanged();
        }
    }

    /// <inheritdoc />
    public async Task CreateBranchAtCommitAsync(
        IRepositorySession session,
        string branchName,
        string commitSha,
        bool checkout = true)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CreateBranchAtCommitAsync(session.RepositoryPath, branchName, commitSha, checkout);
        _eventHub.NotifyBranchesChanged();
        if (checkout)
        {
            _eventHub.NotifyWorkingDirectoryChanged();
            _eventHub.NotifyCommitHistoryChanged();
        }
    }

    /// <inheritdoc />
    public async Task CheckoutAsync(IRepositorySession session, string branchName, bool allowConflicts = false)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CheckoutAsync(session.RepositoryPath, branchName, allowConflicts);
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyCommitHistoryChanged();
    }

    /// <inheritdoc />
    public async Task CheckoutCommitAsync(IRepositorySession session, string commitSha)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.CheckoutCommitAsync(session.RepositoryPath, commitSha);
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyWorkingDirectoryChanged();
        _eventHub.NotifyCommitHistoryChanged();
    }

    /// <inheritdoc />
    public async Task RenameBranchAsync(IRepositorySession session, string oldName, string newName)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.RenameBranchAsync(session.RepositoryPath, oldName, newName);
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task DeleteBranchAsync(IRepositorySession session, string branchName, bool force = false)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DeleteBranchAsync(session.RepositoryPath, branchName, force);
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task DeleteRemoteBranchAsync(
        IRepositorySession session,
        string remoteName,
        string branchName,
        string? username = null,
        string? password = null)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.DeleteRemoteBranchAsync(
            session.RepositoryPath, remoteName, branchName, username, password);
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task SetUpstreamAsync(
        IRepositorySession session,
        string branchName,
        string remoteName,
        string remoteBranchName)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.SetUpstreamAsync(session.RepositoryPath, branchName, remoteName, remoteBranchName);
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task PullBranchFastForwardAsync(
        IRepositorySession session,
        string branchName,
        string remoteName,
        string remoteBranchName,
        bool isCurrentBranch)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.PullBranchFastForwardAsync(
            session.RepositoryPath, branchName, remoteName, remoteBranchName, isCurrentBranch);
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyCommitHistoryChanged();
        if (isCurrentBranch)
        {
            _eventHub.NotifyWorkingDirectoryChanged();
        }
    }

    /// <inheritdoc />
    public async Task PushBranchAsync(
        IRepositorySession session,
        string branchName,
        string remoteName,
        string remoteBranchName,
        bool isCurrentBranch)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.PushBranchAsync(
            session.RepositoryPath, branchName, remoteName, remoteBranchName, isCurrentBranch);
        _eventHub.NotifyBranchesChanged();
    }

    /// <inheritdoc />
    public async Task ResetBranchToCommitAsync(
        IRepositorySession session,
        string branchName,
        string commitSha,
        bool updateWorkingTree)
    {
        session.CancellationToken.ThrowIfCancellationRequested();
        await _gitService.ResetBranchToCommitAsync(
            session.RepositoryPath, branchName, commitSha, updateWorkingTree);
        _eventHub.NotifyBranchesChanged();
        _eventHub.NotifyCommitHistoryChanged();
        if (updateWorkingTree)
        {
            _eventHub.NotifyWorkingDirectoryChanged();
        }
    }
}
