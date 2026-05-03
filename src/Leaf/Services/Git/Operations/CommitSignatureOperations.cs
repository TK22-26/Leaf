using System.Text;
using Leaf.Models;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// §5.8 — enriches a list of commits with GPG/SSH signature data via
/// <c>git log --no-walk --format=...</c>. LibGit2Sharp doesn't expose
/// signature trust state, so the only path to <c>%G?</c> /
/// <c>%GS</c> / <c>%GE</c> / <c>%GF</c> is shelling out to git.
///
/// <para>Stateless and self-contained — operates on a list of SHAs and
/// returns a dictionary keyed by SHA. Caller decides when to invoke
/// (eagerly after history fetch in <see cref="CommitHistoryOperations"/>,
/// lazily on a viewport, or not at all in test fixtures).</para>
/// </summary>
internal class CommitSignatureOperations
{
    private readonly IGitOperationContext _context;

    /// <summary>
    /// Max SHAs per <c>git log</c> invocation. Each SHA is 40 hex chars,
    /// so 100 fits in 4kB — well under the Windows 32k command-line cap
    /// even after the format string and the executable path. Larger
    /// chunks would be marginally faster but blow up on long paths
    /// or proxy wrappers.
    /// </summary>
    private const int ShaChunkSize = 100;

    /// <summary>
    /// Field separator inside one record. <c>\x1f</c> (Unit Separator) is
    /// safe — git never emits it inside <c>%GS</c>/<c>%GE</c>/<c>%GF</c>
    /// values, and the chosen byte stays out of the way of any UTF-8
    /// content git might surface.
    /// </summary>
    private const char FieldSeparator = '\x1f';

    /// <summary>Record separator. <c>\x1e</c> for the same reason.</summary>
    private const char RecordSeparator = '\x1e';

    public CommitSignatureOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Look up signature data for the given SHAs. Returns a dictionary
    /// keyed by SHA. SHAs that git rejects (orphaned, malformed) are
    /// silently dropped — callers should treat absence as "no signature
    /// information available" rather than "no signature".
    /// </summary>
    public async Task<IReadOnlyDictionary<string, CommitSignatureData>> GetSignaturesAsync(
        string repoPath,
        IReadOnlyList<string> shas,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, CommitSignatureData>(shas.Count, StringComparer.OrdinalIgnoreCase);
        if (shas.Count == 0) return result;

        // Chunked invocation. Each chunk runs serially — git log spawns
        // one process per call, and shooting them off in parallel would
        // hammer the working tree's index lock for no real win.
        for (int i = 0; i < shas.Count; i += ShaChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = shas.Skip(i).Take(ShaChunkSize).ToList();
            await PopulateChunkAsync(repoPath, chunk, result, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private async Task PopulateChunkAsync(
        string repoPath,
        IReadOnlyList<string> shas,
        Dictionary<string, CommitSignatureData> sink,
        CancellationToken cancellationToken)
    {
        // Build args: ["log", "--no-walk", "--format=...", sha1, sha2, ...]
        // %H + %G? + %GS + %GE + %GF separated by FieldSeparator and
        // terminated by RecordSeparator + LF (LF for cross-platform line
        // splitting, RecordSeparator so an embedded LF in %GS doesn't
        // confuse the parser).
        var formatString = $"--format=%H{FieldSeparator}%G?{FieldSeparator}%GS{FieldSeparator}%GE{FieldSeparator}%GF{RecordSeparator}";
        var args = new List<string>(shas.Count + 3) { "log", "--no-walk", formatString };
        args.AddRange(shas);

        var proc = await _context.CommandRunner.RunAsync(
            repoPath, args.ToArray(), cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!proc.Success)
        {
            // Bad SHAs in the chunk — drop the whole chunk. Per CLAUDE.md
            // policy this fails loudly enough at the log layer; the UI
            // shows "no signature info" which is the safest fallback.
            Log.Warn("Signing", $"git log signature query failed for {shas.Count} commits: {proc.StandardError.Trim()}");
            return;
        }

        ParseRecords(proc.StandardOutput, sink);
    }

    /// <summary>
    /// Parse the chunk's stdout into <see cref="CommitSignatureData"/>
    /// records. Public-internal for unit testing — the parser does the
    /// real work and is the most regression-prone part of the pipeline.
    /// </summary>
    internal static void ParseRecords(string output, Dictionary<string, CommitSignatureData> sink)
    {
        if (string.IsNullOrEmpty(output)) return;

        // Split on RecordSeparator. Records may include the trailing LF
        // from git's default line ending; strip it so SHA/field parsing
        // doesn't pick up stray \n.
        foreach (var rawRecord in output.Split(RecordSeparator))
        {
            var record = rawRecord.Trim('\r', '\n');
            if (record.Length == 0) continue;

            var parts = record.Split(FieldSeparator);
            if (parts.Length < 5) continue;

            var sha = parts[0].Trim();
            if (sha.Length == 0) continue;

            sink[sha] = new CommitSignatureData(
                Status: ParseTrustCode(parts[1]),
                SignerName: parts[2],
                SignerEmail: parts[3],
                Fingerprint: parts[4]);
        }
    }

    /// <summary>
    /// Map <c>git log %G?</c> trust codes to <see cref="CommitSignatureStatus"/>.
    /// Unknown codes fall through to <see cref="CommitSignatureStatus.None"/>
    /// because returning a positive verification status for a code we
    /// don't understand would be a privilege escalation in UI terms.
    ///
    /// <para>Note on <c>U</c> vs <c>E</c> — git's man page is the source
    /// of truth and they have specifically opposite meanings to what an
    /// English speaker would guess:</para>
    /// <list type="bullet">
    /// <item><c>U</c> = good signature with <b>unknown validity</b> — the
    /// key IS in the keyring but the web of trust hasn't reached it. We
    /// surface this as <see cref="CommitSignatureStatus.UntrustedKey"/>.</item>
    /// <item><c>E</c> = the signature <b>cannot be checked</b>, most often
    /// because the key isn't in the local keyring at all. We surface this
    /// as <see cref="CommitSignatureStatus.UnknownKey"/>.</item>
    /// </list>
    /// </summary>
    internal static CommitSignatureStatus ParseTrustCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return CommitSignatureStatus.None;
        return code.Trim() switch
        {
            "G" => CommitSignatureStatus.Valid,
            "U" => CommitSignatureStatus.UntrustedKey,
            "X" => CommitSignatureStatus.Expired,
            "Y" => CommitSignatureStatus.ExpiredKey,
            "R" => CommitSignatureStatus.RevokedKey,
            "B" => CommitSignatureStatus.Bad,
            "E" => CommitSignatureStatus.UnknownKey,
            "N" => CommitSignatureStatus.None,
            _ => CommitSignatureStatus.None,
        };
    }
}

/// <summary>
/// Parsed signature record for a single commit. Plain record so callers
/// can decide whether to copy fields onto a <see cref="CommitInfo"/> or
/// surface them through some other model (e.g. a tooltip view-model).
/// </summary>
internal sealed record CommitSignatureData(
    CommitSignatureStatus Status,
    string SignerName,
    string SignerEmail,
    string Fingerprint);
