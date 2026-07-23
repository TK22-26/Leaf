#nullable enable
using System.Collections.Concurrent;
using Leaf.Models;

namespace Leaf.Services.Merge;

/// <summary>
/// <see cref="IMergeBlameService"/> implementation backed by
/// <see cref="IGitService.GetFileBlameAsync"/> and a two-level cache:
/// the outer key (<c>repoPath</c>, <c>filePath</c>) tracks the per-file
/// blame dictionaries; the inner key is <c>lineNumber</c>. Entries are
/// tagged with the HEAD sha captured at fetch time so a ref update
/// between hovers invalidates without an explicit eviction hook.
/// </summary>
/// <remarks>
/// Thread-safety: hover events fire on the UI dispatcher but the blame
/// fetch itself runs async through <see cref="IGitService"/>, so multiple
/// concurrent lookups for the same file can race into
/// <see cref="GetLineBlameAsync"/>. Fetch is guarded by a per-file
/// <see cref="SemaphoreSlim"/> so only one git subprocess runs per
/// file+head-sha pair — subsequent waiters get the cached result.
/// </remarks>
public sealed class MergeBlameService : IMergeBlameService
{
    private readonly IGitService _git;
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<CacheKey, SemaphoreSlim> _fetchGates = new();

    public MergeBlameService(IGitService git)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
    }

    public async Task<FileBlameLine?> GetLineBlameAsync(
        string repoPath,
        string filePath,
        int oneBasedLineNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(repoPath);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        if (oneBasedLineNumber < 1) return null;

        var headSha = await ResolveHeadShaAsync(repoPath, cancellationToken).ConfigureAwait(false);
        if (headSha is null) return null; // unborn / detached pre-commit repo — no blame to show

        // Windows paths are case-insensitive; normalise at the cache boundary
        // so "C:\Foo" and "c:\foo" hit the same entry. Without this, the
        // record-struct's default string equality would key them separately
        // and the second hover would fire a redundant subprocess.
        var key = NormalizeKey(repoPath, filePath);
        if (_cache.TryGetValue(key, out var cached) && cached.HeadSha == headSha)
        {
            return cached.Lines.TryGetValue(oneBasedLineNumber, out var hit) ? hit : null;
        }

        // Per-key fetch gate: serialize the blame call so N concurrent hovers
        // on the same file produce one subprocess, not N. GetOrAdd over the
        // gate dictionary gives us a stable instance per key without a lock.
        var gate = _fetchGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check under the gate — another waiter may have populated
            // the cache while we were waiting on the semaphore.
            if (_cache.TryGetValue(key, out cached) && cached.HeadSha == headSha)
            {
                return cached.Lines.TryGetValue(oneBasedLineNumber, out var hit) ? hit : null;
            }

            var blame = await _git.GetFileBlameAsync(repoPath, filePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var byLine = new Dictionary<int, FileBlameLine>(blame.Count);
            foreach (var line in blame)
            {
                byLine[line.LineNumber] = line;
            }
            _cache[key] = new CacheEntry(headSha, byLine);
            return byLine.TryGetValue(oneBasedLineNumber, out var result) ? result : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public void InvalidateRepo(string repoPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(repoPath);
        var normalizedRepo = NormalizePath(repoPath);
        // Snapshot first — mutating a ConcurrentDictionary's key enumerator
        // mid-iteration is documented to be safe, but paired with the
        // separate _fetchGates dictionary we want to make sure both sides
        // evict the same key set.
        var victims = new List<CacheKey>();
        foreach (var key in _cache.Keys)
        {
            // Keys are already normalized by GetLineBlameAsync — ordinal
            // comparison here matches the same normalization path so a
            // user-supplied differently-cased repoPath still invalidates.
            if (string.Equals(key.RepoPath, normalizedRepo, StringComparison.Ordinal))
            {
                victims.Add(key);
            }
        }
        foreach (var key in victims)
        {
            _cache.TryRemove(key, out _);
            // Evict (but don't Dispose) the per-key fetch gate. An in-flight
            // fetch that's still holding the semaphore would see its
            // subsequent Release throw ObjectDisposedException if we
            // disposed here — a real race even if narrow. Removing the
            // gate from the dictionary is enough: no new waiters can reach
            // it, and GC reclaims the instance after the current holder
            // finishes its finally-block. SemaphoreSlim.Dispose is
            // documented optional for exactly this reason.
            _fetchGates.TryRemove(key, out _);
        }
    }

    private static CacheKey NormalizeKey(string repoPath, string filePath) =>
        new(NormalizePath(repoPath), NormalizePath(filePath));

    private static string NormalizePath(string p)
    {
        // OperatingSystem.IsWindows avoids the hardcoded #if pattern and
        // matches how Leaf handles cross-platform path comparisons elsewhere.
        // On non-Windows the filesystem is case-sensitive and round-tripping
        // through ToUpperInvariant would break, so leave it alone.
        return OperatingSystem.IsWindows() ? p.ToUpperInvariant() : p;
    }

    private async Task<string?> ResolveHeadShaAsync(string repoPath, CancellationToken cancellationToken)
    {
        try
        {
            var head = await _git.GetHeadCommitAsync(repoPath, cancellationToken).ConfigureAwait(false);
            return head?.Sha;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A HEAD-resolve failure shouldn't throw up into the hover debounce
            // — nothing recoverable happens there. Returning null short-circuits
            // the lookup to "no blame available"; the popover stays hidden
            // which is the correct affordance for a repo we can't read.
            // Log the exception *type* only (no repo path / sha / file) so the
            // privacy contract from the Phase-5 telemetry gate stays intact.
            Log.Info("MergeBlame", $"HeadResolveFailed: {ex.GetType().Name}");
            return null;
        }
    }

    private readonly record struct CacheKey(string RepoPath, string FilePath);

    private sealed record CacheEntry(string HeadSha, IReadOnlyDictionary<int, FileBlameLine> Lines);
}
