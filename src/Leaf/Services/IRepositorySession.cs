using LibGit2Sharp;

namespace Leaf.Services;

/// <summary>
/// Per-repository context that holds all repo-specific state.
/// Disposed when switching repositories to prevent state leakage and circular dependencies.
/// </summary>
/// <remarks>
/// This is the ONLY way to safely access LibGit2Sharp Repository.
/// All operations must go through <see cref="RunWithRepositoryAsync{T}"/>.
/// </remarks>
public interface IRepositorySession : IDisposable
{
    /// <summary>
    /// Working directory path (or bare repo root for bare repositories).
    /// </summary>
    string RepositoryPath { get; }

    /// <summary>
    /// Path to the .git folder (or bare repo root for bare repositories).
    /// </summary>
    string GitDirectory { get; }

    /// <summary>
    /// True if repository is valid and accessible.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    /// True after Dispose() has been called.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// True if this is a bare repository (no working directory).
    /// </summary>
    bool IsBareRepository { get; }

    /// <summary>
    /// Cancellation token that is cancelled when the session is disposed.
    /// Pass this to all async operations to enable cancellation on repository switch.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Generation number for stale result detection.
    /// Increments each time a new session is created.
    /// </summary>
    long Generation { get; }

    /// <summary>
    /// Execute an operation with the repository lock held.
    /// This is THE ONLY way to access LibGit2Sharp Repository safely.
    /// </summary>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <param name="operation">Function that receives the Repository and returns a result.</param>
    /// <param name="ct">Optional additional cancellation token to combine with session token.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="ObjectDisposedException">Session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Operation was cancelled.</exception>
    Task<T> RunWithRepositoryAsync<T>(Func<Repository, T> operation, CancellationToken ct = default);

    /// <summary>
    /// Execute an operation with the repository lock held.
    /// This is THE ONLY way to access LibGit2Sharp Repository safely.
    /// </summary>
    /// <param name="operation">Action that receives the Repository.</param>
    /// <param name="ct">Optional additional cancellation token to combine with session token.</param>
    /// <exception cref="ObjectDisposedException">Session has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Operation was cancelled.</exception>
    Task RunWithRepositoryAsync(Action<Repository> operation, CancellationToken ct = default);
}
