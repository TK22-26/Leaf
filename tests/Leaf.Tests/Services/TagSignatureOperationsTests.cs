using FluentAssertions;
using Leaf.Models;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Pure-logic tests for <see cref="TagSignatureOperations"/> — the
/// record parser. The CLI-driven path (running git for-each-ref) needs
/// a real repo and lives in the integration suite.
/// </summary>
public class TagSignatureOperationsTests
{
    private const string FS = "";
    private const string RS = "";

    [Fact]
    public void ParseRecords_SignedTag()
    {
        var output = $"v1.0{FS}G{FS}Alice <alice@example.com>{FS}AAA1234{RS}";
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(output, sink);

        sink.Should().ContainKey("v1.0");
        var data = sink["v1.0"];
        data.Status.Should().Be(CommitSignatureStatus.Valid);
        data.SignerName.Should().Be("Alice");
        data.SignerEmail.Should().Be("alice@example.com");
        data.Fingerprint.Should().Be("AAA1234");
    }

    [Fact]
    public void ParseRecords_UnsignedTag()
    {
        // for-each-ref returns empty fields for the signature placeholders
        // when a tag is unsigned. The parser maps that to None status.
        var output = $"v0.1{FS}{FS}{FS}{RS}";
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(output, sink);

        sink["v0.1"].Status.Should().Be(CommitSignatureStatus.None);
        sink["v0.1"].SignerName.Should().Be(string.Empty);
        sink["v0.1"].SignerEmail.Should().Be(string.Empty);
        sink["v0.1"].Fingerprint.Should().Be(string.Empty);
    }

    [Fact]
    public void ParseRecords_MultipleTags()
    {
        var output =
            $"v1.0{FS}G{FS}Alice <a@x>{FS}AAA{RS}" +
            $"v1.1{FS}U{FS}Bob <b@y>{FS}BBB{RS}" +
            $"v1.2{FS}{FS}{FS}{RS}";
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(output, sink);

        sink.Should().HaveCount(3);
        sink["v1.0"].Status.Should().Be(CommitSignatureStatus.Valid);
        sink["v1.1"].Status.Should().Be(CommitSignatureStatus.UntrustedKey);
        sink["v1.2"].Status.Should().Be(CommitSignatureStatus.None);
    }

    [Fact]
    public void ParseRecords_HandlesTrailingNewlines()
    {
        // git on Windows often emits \r\n at the end of each record.
        var output =
            $"v1.0{FS}G{FS}Alice <a@x>{FS}AAA{RS}\r\n" +
            $"v1.1{FS}{FS}{FS}{RS}\r\n";
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(output, sink);

        sink.Should().HaveCount(2);
        sink.Should().ContainKey("v1.0");
        sink.Should().ContainKey("v1.1");
    }

    [Fact]
    public void ParseRecords_EmptyInputReturnsEmpty()
    {
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(string.Empty, sink);
        TagSignatureOperations.ParseRecords("\r\n", sink);
        sink.Should().BeEmpty();
    }

    [Fact]
    public void ParseRecords_SkipsMalformedRecord()
    {
        // Two-field record (missing signer / fingerprint) — git would
        // never emit this shape, but a future format change shouldn't
        // crash the parser. Skip silently.
        var output = $"v1.0{FS}G{RS}";
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(output, sink);
        sink.Should().BeEmpty();
    }

    [Fact]
    public void ParseRecords_SshSignatureWithoutEmailPart()
    {
        // SSH signers don't have <email> brackets — the splitter drops
        // the email and keeps the whole string as the signer name.
        var output = $"v2.0{FS}G{FS}ssh-ed25519 AAAAC3...{FS}SHA256:abcd{RS}";
        var sink = new Dictionary<string, TagSignatureData>(StringComparer.Ordinal);
        TagSignatureOperations.ParseRecords(output, sink);

        sink["v2.0"].SignerName.Should().Be("ssh-ed25519 AAAAC3...");
        sink["v2.0"].SignerEmail.Should().BeEmpty();
        sink["v2.0"].Fingerprint.Should().Be("SHA256:abcd");
    }
}
