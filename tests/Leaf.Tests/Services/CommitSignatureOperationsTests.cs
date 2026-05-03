using FluentAssertions;
using Leaf.Models;
using Leaf.Services.Git.Operations;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Pure-logic tests for <see cref="CommitSignatureOperations"/>: the
/// trust-code mapping and the record-stream parser. The CLI-driven
/// path (running `git log` in a real repo) needs an integration
/// fixture and lives elsewhere.
/// </summary>
public class CommitSignatureOperationsTests
{
    //  (Unit Separator) and  (Record Separator) match the
    // delimiters CommitSignatureOperations writes into its --format
    // string. Spelled out as 4-digit \u escapes to dodge C#'s greedy
    // \x parser — \x1fG would be ambiguous because G isn't hex but the
    // hex-prefix rules across .NET versions trip on adjacent hex chars
    // like \x1fA.
    private const string FS = "";
    private const string RS = "";

    [Theory]
    [InlineData("G", CommitSignatureStatus.Valid)]
    [InlineData("U", CommitSignatureStatus.UnknownKey)]
    [InlineData("X", CommitSignatureStatus.Expired)]
    [InlineData("Y", CommitSignatureStatus.ExpiredKey)]
    [InlineData("R", CommitSignatureStatus.RevokedKey)]
    [InlineData("B", CommitSignatureStatus.Bad)]
    [InlineData("E", CommitSignatureStatus.UntrustedKey)]
    [InlineData("N", CommitSignatureStatus.None)]
    public void ParseTrustCode_KnownCodes(string code, CommitSignatureStatus expected)
    {
        CommitSignatureOperations.ParseTrustCode(code).Should().Be(expected);
    }

    [Fact]
    public void ParseTrustCode_UnknownDefaultsToNone()
    {
        // Future git versions could add new codes; until we map them, fall
        // back to None rather than mis-reporting "Verified".
        CommitSignatureOperations.ParseTrustCode("Z").Should().Be(CommitSignatureStatus.None);
        CommitSignatureOperations.ParseTrustCode("").Should().Be(CommitSignatureStatus.None);
        CommitSignatureOperations.ParseTrustCode(" ").Should().Be(CommitSignatureStatus.None);
    }

    [Fact]
    public void ParseRecords_SingleSignedRecord()
    {
        var output = $"abc123{FS}G{FS}Alice{FS}alice@example.com{FS}ABCD1234EFFE5678{RS}";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);

        sink.Should().ContainKey("abc123");
        var data = sink["abc123"];
        data.Status.Should().Be(CommitSignatureStatus.Valid);
        data.SignerName.Should().Be("Alice");
        data.SignerEmail.Should().Be("alice@example.com");
        data.Fingerprint.Should().Be("ABCD1234EFFE5678");
    }

    [Fact]
    public void ParseRecords_UnsignedRecordHasEmptyFields()
    {
        var output = $"deadbeef{FS}N{FS}{FS}{FS}{RS}";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);

        sink["deadbeef"].Status.Should().Be(CommitSignatureStatus.None);
        sink["deadbeef"].SignerName.Should().Be(string.Empty);
        sink["deadbeef"].SignerEmail.Should().Be(string.Empty);
        sink["deadbeef"].Fingerprint.Should().Be(string.Empty);
    }

    [Fact]
    public void ParseRecords_MultipleRecords()
    {
        var output =
            $"sha1{FS}G{FS}Alice{FS}alice@example.com{FS}AAA{RS}" +
            $"sha2{FS}U{FS}Bob{FS}bob@example.com{FS}BBB{RS}" +
            $"sha3{FS}N{FS}{FS}{FS}{RS}";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);

        sink.Should().HaveCount(3);
        sink["sha1"].Status.Should().Be(CommitSignatureStatus.Valid);
        sink["sha2"].Status.Should().Be(CommitSignatureStatus.UnknownKey);
        sink["sha3"].Status.Should().Be(CommitSignatureStatus.None);
    }

    [Fact]
    public void ParseRecords_HandlesTrailingNewlines()
    {
        // git on Windows often emits \r\n between records; the parser
        // trims those off the SHA so the dictionary key is clean.
        var output =
            $"sha1{FS}G{FS}Alice{FS}a@x{FS}AAA{RS}\r\n" +
            $"sha2{FS}N{FS}{FS}{FS}{RS}\r\n";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);

        sink.Should().HaveCount(2);
        sink.Should().ContainKey("sha1");
        sink.Should().ContainKey("sha2");
    }

    [Fact]
    public void ParseRecords_EmptyOutputReturnsEmpty()
    {
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(string.Empty, sink);
        CommitSignatureOperations.ParseRecords("\r\n", sink);
        sink.Should().BeEmpty();
    }

    [Fact]
    public void ParseRecords_SkipsMalformedRecord()
    {
        // Two-field record (missing signer/email/fingerprint) is invalid;
        // skip silently rather than throwing, since git's output format
        // could change in a future version and we want graceful degradation.
        var output = $"sha1{FS}G{RS}";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);
        sink.Should().BeEmpty();
    }
}
