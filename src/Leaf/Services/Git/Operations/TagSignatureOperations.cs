using Leaf.Models;
using Leaf.Services.Git.Core;

namespace Leaf.Services.Git.Operations;

/// <summary>
/// §5.8 tag signing — enriches <see cref="TagInfo"/> with GPG/SSH
/// signature data via <c>git for-each-ref refs/tags/</c>. The
/// <c>%(signature:grade)</c> / <c>%(signature:signer)</c> /
/// <c>%(signature:key)</c> placeholders return the same information
/// commits get from <c>%G?</c>/<c>%GS</c>/<c>%GF</c>, but they're tag-
/// specific (commit-style placeholders return empty for tag refs).
///
/// <para>One git invocation lists all tags + all signature data —
/// cheaper than the per-tag <c>git verify-tag</c> spawn loop the first
/// pass would have used. Stateless service; lifetime parallels
/// <see cref="CommitSignatureOperations"/>.</para>
/// </summary>
internal class TagSignatureOperations
{
    private readonly IGitOperationContext _context;

    /// <summary>
    /// Field separator inside one record. <c></c> (Unit Separator)
    /// — git never emits it inside signer / fingerprint / message values.
    /// </summary>
    private const char FieldSeparator = '';

    /// <summary>Record separator. <c></c> for the same reason.</summary>
    private const char RecordSeparator = '';

    public TagSignatureOperations(IGitOperationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Look up signature data for every tag in the repository, keyed by
    /// tag name. Tags that <c>for-each-ref</c> doesn't return get nothing
    /// in the dictionary — caller treats absence as "no signature info
    /// available". Failures are non-fatal — graph badges just don't
    /// appear when this query 500s.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, TagSignatureData>> GetTagSignaturesAsync(
        string repoPath,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);

        // %(refname:strip=2) → "v1.0" (strip refs/tags/)
        // %(signature:grade) → G/U/B/X/Y/R/E/N (same alphabet as %G? for commits)
        // %(signature:signer) → "Name <email>" for GPG; key comment for SSH
        // %(signature:key)    → fingerprint for GPG; SHA256:… for SSH
        //
        // signature:* expansions were added in git 2.27 (May 2020). We
        // don't gate on git version — older gits return empty strings
        // for the placeholders, which the parser treats as "no signature
        // data" (i.e. None status), which is the correct fallback.
        var formatString =
            $"--format=%(refname:strip=2){FieldSeparator}" +
            $"%(signature:grade){FieldSeparator}" +
            $"%(signature:signer){FieldSeparator}" +
            $"%(signature:key){RecordSeparator}";

        var args = new[] { "for-each-ref", "refs/tags/", formatString };
        var proc = await _context.CommandRunner.RunAsync(repoPath, args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!proc.Success)
        {
            Log.Warn("Signing", $"git for-each-ref signature query failed: {proc.StandardError.Trim()}");
            return result;
        }

        ParseRecords(proc.StandardOutput, result);
        return result;
    }

    /// <summary>
    /// Parse the chunk's stdout into <see cref="TagSignatureData"/>
    /// records. Public-internal for unit testing — same shape as the
    /// commit-side parser so the regressions live next door.
    /// </summary>
    internal static void ParseRecords(string output, Dictionary<string, TagSignatureData> sink)
    {
        if (string.IsNullOrEmpty(output)) return;

        foreach (var rawRecord in output.Split(RecordSeparator))
        {
            var record = rawRecord.Trim('\r', '\n');
            if (record.Length == 0) continue;

            var parts = record.Split(FieldSeparator);
            if (parts.Length < 4) continue;

            var name = parts[0].Trim();
            if (name.Length == 0) continue;

            // Reuse the splitter from CommitSignatureOperations so
            // GPG ("Name <email>") and SSH (raw key comment) shapes
            // stay consistent across commits and tags.
            CommitSignatureOperations.SplitSignerNameEmail(parts[2], out var signerName, out var signerEmail);

            sink[name] = new TagSignatureData(
                Status: CommitSignatureOperations.ParseTrustCode(parts[1]),
                SignerName: signerName,
                SignerEmail: signerEmail,
                Fingerprint: parts[3]);
        }
    }
}

/// <summary>
/// Parsed signature record for a single tag. Keyed on tag name (not SHA)
/// because <c>for-each-ref refs/tags/</c> uses the ref shortname as the
/// natural identifier — and unlike commits, two tags can point at the
/// same SHA legitimately, so SHA isn't unique.
/// </summary>
internal sealed record TagSignatureData(
    CommitSignatureStatus Status,
    string SignerName,
    string SignerEmail,
    string Fingerprint);
