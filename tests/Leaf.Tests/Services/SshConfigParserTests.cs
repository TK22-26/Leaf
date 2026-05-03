using FluentAssertions;
using Leaf.Services.Ssh;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Parser tests for <see cref="SshConfigParser"/>. Covers the round-trip
/// guarantee (parse → write → parse produces the same hosts), tolerance
/// of mixed line endings and equals-sign syntax, and the round-trip of
/// <c>Match</c> / unknown options that the editor doesn't model.
/// </summary>
public class SshConfigParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsNoHosts()
    {
        var parsed = SshConfigParser.Parse(string.Empty);
        parsed.Hosts.Should().BeEmpty();
        parsed.LeadingPreamble.Should().BeEmpty();
        parsed.MatchTail.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SingleHost_ReadsAllKnownKeywords()
    {
        var input = """
            Host github.com
                HostName github.com
                User git
                Port 22
                IdentityFile ~/.ssh/id_ed25519
                ProxyCommand none
            """;
        var parsed = SshConfigParser.Parse(input);
        parsed.Hosts.Should().HaveCount(1);
        var entry = parsed.Hosts[0];
        entry.HostPattern.Should().Be("github.com");
        entry.HostName.Should().Be("github.com");
        entry.User.Should().Be("git");
        entry.Port.Should().Be(22);
        entry.IdentityFile.Should().Be("~/.ssh/id_ed25519");
        entry.ProxyCommand.Should().Be("none");
        entry.ExtraOptions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_EqualsSignSeparator_IsAccepted()
    {
        // OpenSSH allows either `Keyword Value` or `Keyword=Value`.
        var input = "Host gitlab\n    HostName=gitlab.com\n    User=git\n";
        var parsed = SshConfigParser.Parse(input);
        var entry = parsed.Hosts.Single();
        entry.HostName.Should().Be("gitlab.com");
        entry.User.Should().Be("git");
    }

    [Fact]
    public void Parse_PreservesUnknownOptionsAsExtras()
    {
        var input = """
            Host bitbucket.org
                HostName bitbucket.org
                User git
                ServerAliveInterval 60
                AddKeysToAgent yes
            """;
        var entry = SshConfigParser.Parse(input).Hosts.Single();
        entry.ExtraOptions.Should().HaveCount(2);
        entry.ExtraOptions[0].Key.Should().Be("ServerAliveInterval");
        entry.ExtraOptions[0].Value.Should().Be("60");
        entry.ExtraOptions[1].Key.Should().Be("AddKeysToAgent");
        entry.ExtraOptions[1].Value.Should().Be("yes");
    }

    [Fact]
    public void Parse_PreservesLeadingCommentsAsPreamble()
    {
        var input = """
            # SSH config managed by Leaf
            Include /etc/ssh/extra.conf

            Host github.com
                HostName github.com
            """;
        var parsed = SshConfigParser.Parse(input);
        parsed.LeadingPreamble.Should().Contain("Include /etc/ssh/extra.conf");
        parsed.LeadingPreamble.Should().Contain("# SSH config managed by Leaf");
        parsed.Hosts.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_MatchStanza_RoundTripsViaTail()
    {
        var input = """
            Host github.com
                HostName github.com

            Match host *.internal
                IdentityFile ~/.ssh/internal-only
                ForwardAgent no
            """;
        var parsed = SshConfigParser.Parse(input);
        parsed.Hosts.Should().HaveCount(1);
        parsed.MatchTail.Should().Contain("Match host *.internal");
        parsed.MatchTail.Should().Contain("ForwardAgent no");
    }

    [Fact]
    public void Parse_MultipleHosts_AreIndependent()
    {
        var input = """
            Host a
                HostName a.example.com
                Port 22

            Host b
                HostName b.example.com
                Port 2222
                User bob
            """;
        var parsed = SshConfigParser.Parse(input);
        parsed.Hosts.Should().HaveCount(2);
        parsed.Hosts[0].HostName.Should().Be("a.example.com");
        parsed.Hosts[0].Port.Should().Be(22);
        parsed.Hosts[1].HostName.Should().Be("b.example.com");
        parsed.Hosts[1].Port.Should().Be(2222);
        parsed.Hosts[1].User.Should().Be("bob");
    }

    [Fact]
    public void Parse_HandlesCrLfLineEndings()
    {
        var input = "Host github.com\r\n    HostName github.com\r\n    User git\r\n";
        var parsed = SshConfigParser.Parse(input);
        parsed.Hosts.Should().HaveCount(1);
        parsed.Hosts[0].HostName.Should().Be("github.com");
    }

    [Fact]
    public void Write_RoundTrips_KnownAndExtraOptions()
    {
        var entries = new[]
        {
            new SshConfigEntry
            {
                HostPattern = "github.com",
                HostName = "github.com",
                User = "git",
                Port = 22,
                IdentityFile = "~/.ssh/id_ed25519",
                ExtraOptions = [new SshConfigOption("AddKeysToAgent", "yes")],
            },
        };
        var serialized = SshConfigParser.Write(new SshConfigParser.ParsedConfig(string.Empty, entries, string.Empty));
        var reparsed = SshConfigParser.Parse(serialized);
        reparsed.Hosts.Should().HaveCount(1);
        var entry = reparsed.Hosts[0];
        entry.HostPattern.Should().Be("github.com");
        entry.HostName.Should().Be("github.com");
        entry.User.Should().Be("git");
        entry.Port.Should().Be(22);
        entry.IdentityFile.Should().Be("~/.ssh/id_ed25519");
        entry.ExtraOptions.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SshConfigOption("AddKeysToAgent", "yes"));
    }

    [Fact]
    public void Write_PreservesPreambleAndMatchTail()
    {
        var preamble = "# Leaf-managed config\nInclude /etc/ssh/extra.conf";
        var matchTail = "Match host *.internal\n    IdentityFile ~/.ssh/internal-only";
        var entries = new[]
        {
            new SshConfigEntry { HostPattern = "host1", HostName = "host1.example.com" },
        };
        var serialized = SshConfigParser.Write(new SshConfigParser.ParsedConfig(preamble, entries, matchTail));
        serialized.Should().Contain("Include /etc/ssh/extra.conf");
        serialized.Should().Contain("Match host *.internal");
        serialized.Should().Contain("Host host1");
    }

    [Fact]
    public void Parse_HostWithMultiplePatterns_KeepsPatternVerbatim()
    {
        // OpenSSH allows `Host github.com gitlab.com`. We surface that
        // as one entry with the whole pattern intact rather than
        // splitting into two — splitting would break the user's intent
        // of sharing settings between hosts.
        var input = "Host github.com gitlab.com\n    User git\n";
        var entry = SshConfigParser.Parse(input).Hosts.Single();
        entry.HostPattern.Should().Be("github.com gitlab.com");
        entry.User.Should().Be("git");
    }
}
