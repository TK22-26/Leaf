using LibGit2Sharp;

namespace Leaf.Services;

/// <summary>
/// Per-repository context that holds all repo-specific state.
/// Thread-safe through operation serialization via semaphore.
/// </summary>
public sealed class RepositorySession : IRepositorySession
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _initLock = new();
    private Repository? _repo;
    private int _disposeRequested;

    public RepositorySession(string workingPath, string gitDir, bool isBare, long generation)
    {
        RepositoryPath = workingPath ?? throw new ArgumentNullException(nameof(workingPath));
        GitDirectory = gitDir ?? throw new ArgumentNullException(nameof(gitDir));
        IsBareRepository = isBare;
        Generation = generation;
        IsValid = true;
    }

    public string RepositoryPath { get; }
    public string GitDirectory { get; }
    public bool IsBareRepository { get; }
    public long Generation { get; }
    public CancellationToken CancellationToken => _cts.Token;
    public bool IsDisposed => _disposeRequested == 1;
    public bool IsValid { get; private set; }

    /// <summary>
    /// Gets or creates the Repository instance.
    /// Repository access is ONLY through RunWithRepositoryAsync - this is private.
    /// </summary>
    private Repository GetOrCreateRepository()
    {
        if (_repo != null) return _repo;

        lock (_initLock)
        {
            if (_repo != null) return _repo;

            try
            {
                _repo = new Repository(GitDirectory);
                return _repo;
            }
            catch (RepositoryNotFoundException ex)
            {
                IsValid = false;
                throw new InvalidOperationException(
                    $"Repository at '{RepositoryPath}' is no longer valid", ex);
            }
        }
    }

    /// <inheritdoc />
    public async Task<T> RunWithRepositoryAsync<T>(
        Func<Repository, T> operation,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        // Combine session token with caller's token
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);

        await _operationGate.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        try
        {
            linkedCts.Token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            var repo = GetOrCreateRepository();
            return operation(repo);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RunWithRepositoryAsync(
        Action<Repository> operation,
        CancellationToken ct = default)
    {
        await RunWithRepositoryAsync(repo =>
        {
            operation(repo);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the session, cancelling all in-flight operations.
    /// Disposal is idempotent and non-blocking (cleanup runs in background).
    /// </summary>
    public void Dispose()
    {
        // Idempotent - only first call proceeds
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 1)
            return;

        // Cancel immediately (non-blocking) - this will cause in-flight
        // operations to receive OperationCanceledException
        _cts.Cancel();

        // Drain in background to avoid blocking UI while remaining safe.
        // Acquire gate before disposing repo to ensure no operation is running.
        _ = Task.Run(() =>
        {
            try
            {
                // Wait for in-flight operations to complete
                _operationGate.Wait();
                try
                {
                    _repo?.Dispose();
                    _repo = null;
                }
                finally
                {
                    _operationGate.Release();
                    _operationGate.Dispose();
                    _cts.Dispose();
                }
            }
            catch
            {
                // Suppress exceptions during disposal - we're shutting down
            }
        });
    }
}
