using System.Globalization;
using System.Text;

namespace Leaf.Services.Ssh;

/// <summary>
/// Parser/writer for <c>~/.ssh/config</c>. The OpenSSH config grammar is
/// simple: line-based, <c>Keyword Value</c> pairs, comments start with
/// <c>#</c>, and a <c>Host</c> line opens a new stanza that consumes
/// every following line until the next <c>Host</c> or <c>Match</c>.
///
/// <para>We deliberately do not honour <c>Include</c> directives: the
/// editor's mental model is "the file on disk", and resolving includes
/// would mean writing them back, which produces edits the user didn't
/// ask for. <c>Include</c> lines round-trip verbatim through the
/// "global" stanza preamble.</para>
///
/// <para><c>Match</c> stanzas are similarly preserved verbatim — we
/// don't model them, we don't edit them, but their text isn't lost on
/// rewrite.</para>
/// </summary>
internal static class SshConfigParser
{
    /// <summary>
    /// Parsed view of an SSH config file. <see cref="Hosts"/> is the
    /// editable surface; <see cref="LeadingPreamble"/> is everything
    /// before the first <c>Host</c> line (comments, includes, global
    /// settings) and is round-tripped untouched. <see cref="MatchTail"/>
    /// captures any <c>Match</c> stanza we encountered — it appears at
    /// the end of the rewritten file in its original form.
    /// </summary>
    public sealed record ParsedConfig(
        string LeadingPreamble,
        IReadOnlyList<SshConfigEntry> Hosts,
        string MatchTail);

    /// <summary>
    /// Parse the entire config text into a <see cref="ParsedConfig"/>.
    /// Tolerant of blank input, mixed line endings, and missing trailing
    /// newlines.
    /// </summary>
    public static ParsedConfig Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new ParsedConfig(string.Empty, [], string.Empty);

        var lines = text.Replace("\r\n", "\n").Split('\n');

        var preamble = new StringBuilder();
        var hosts = new List<SshConfigEntry>();
        var matchTail = new StringBuilder();

        // State machine: 0 = preamble, 1 = inside a Host, 2 = inside a Match (preserve verbatim).
        int state = 0;
        SshConfigEntryBuilder? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r', ' ', '\t');
            var lineForMatch = line.TrimStart();

            if (state == 2)
            {
                matchTail.Append(rawLine).Append('\n');
                continue;
            }

            // Comments and blank lines belong to whichever state we're in.
            // For preamble we keep them verbatim; for hosts we drop them
            // (the writer emits a clean canonical layout).
            if (lineForMatch.Length == 0 || lineForMatch.StartsWith('#'))
            {
                if (state == 0)
                    preamble.Append(rawLine).Append('\n');
                continue;
            }

            var (keyword, value) = SplitKeywordValue(lineForMatch);
            if (keyword.Length == 0) continue;

            if (string.Equals(keyword, "Match", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent(current, hosts);
                current = null;
                state = 2;
                matchTail.Append(rawLine).Append('\n');
                continue;
            }

            if (string.Equals(keyword, "Host", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrent(current, hosts);
                current = new SshConfigEntryBuilder { HostPattern = value };
                state = 1;
                continue;
            }

            if (state == 0)
            {
                // Pre-Host directive. Preserve the original text so
                // Include / global Host * settings round-trip.
                preamble.Append(rawLine).Append('\n');
                continue;
            }

            // Host body — populate the current entry.
            if (current is null) continue;
            ApplyOption(current, keyword, value);
        }

        FlushCurrent(current, hosts);

        return new ParsedConfig(
            LeadingPreamble: preamble.ToString().TrimEnd('\n'),
            Hosts: hosts,
            MatchTail: matchTail.ToString().TrimEnd('\n'));
    }

    /// <summary>
    /// Render <paramref name="config"/> back to text. The output uses
    /// LF line endings — Windows OpenSSH reads either, and writing CRLF
    /// would create a noisy diff for users who originally edited the
    /// file under WSL/Linux/macOS.
    /// </summary>
    public static string Write(ParsedConfig config)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(config.LeadingPreamble))
        {
            sb.Append(config.LeadingPreamble);
            sb.Append('\n');
            if (config.Hosts.Count > 0 || !string.IsNullOrEmpty(config.MatchTail))
                sb.Append('\n');
        }

        for (int i = 0; i < config.Hosts.Count; i++)
        {
            var entry = config.Hosts[i];
            sb.Append("Host ").Append(entry.HostPattern.Trim()).Append('\n');
            AppendOption(sb, "HostName", entry.HostName);
            AppendOption(sb, "User", entry.User);
            if (entry.Port is { } p)
                sb.Append("    Port ").Append(p.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendOption(sb, "IdentityFile", entry.IdentityFile);
            AppendOption(sb, "ProxyCommand", entry.ProxyCommand);
            foreach (var extra in entry.ExtraOptions)
                AppendOption(sb, extra.Key, extra.Value);
            if (i < config.Hosts.Count - 1) sb.Append('\n');
        }

        if (!string.IsNullOrEmpty(config.MatchTail))
        {
            if (config.Hosts.Count > 0) sb.Append('\n');
            sb.Append(config.MatchTail);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void AppendOption(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("    ").Append(key).Append(' ').Append(value.Trim()).Append('\n');
    }

    private static (string Keyword, string Value) SplitKeywordValue(string line)
    {
        // OpenSSH allows `Keyword=Value` as well as `Keyword Value`.
        // Whitespace and `=` are both separators; we collapse them.
        int splitAt = -1;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == ' ' || line[i] == '\t' || line[i] == '=')
            {
                splitAt = i;
                break;
            }
        }
        if (splitAt < 0) return (line, string.Empty);

        var keyword = line[..splitAt];
        // Skip ALL contiguous whitespace / `=` separators.
        var rest = line[(splitAt + 1)..].TrimStart(' ', '\t', '=');
        return (keyword, rest);
    }

    private static void ApplyOption(SshConfigEntryBuilder entry, string keyword, string value)
    {
        if (string.Equals(keyword, "HostName", StringComparison.OrdinalIgnoreCase))
            entry.HostName = value;
        else if (string.Equals(keyword, "User", StringComparison.OrdinalIgnoreCase))
            entry.User = value;
        else if (string.Equals(keyword, "Port", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            entry.Port = port;
        else if (string.Equals(keyword, "IdentityFile", StringComparison.OrdinalIgnoreCase))
            entry.IdentityFile = value;
        else if (string.Equals(keyword, "ProxyCommand", StringComparison.OrdinalIgnoreCase))
            entry.ProxyCommand = value;
        else
            entry.Extras.Add(new SshConfigOption(keyword, value));
    }

    private static void FlushCurrent(SshConfigEntryBuilder? current, List<SshConfigEntry> hosts)
    {
        if (current is null) return;
        if (string.IsNullOrWhiteSpace(current.HostPattern)) return;
        hosts.Add(new SshConfigEntry
        {
            HostPattern = current.HostPattern,
            HostName = current.HostName,
            User = current.User,
            Port = current.Port,
            IdentityFile = current.IdentityFile,
            ProxyCommand = current.ProxyCommand,
            ExtraOptions = current.Extras,
        });
    }

    private sealed class SshConfigEntryBuilder
    {
        public string HostPattern = string.Empty;
        public string? HostName;
        public string? User;
        public int? Port;
        public string? IdentityFile;
        public string? ProxyCommand;
        public List<SshConfigOption> Extras = new();
    }
}
