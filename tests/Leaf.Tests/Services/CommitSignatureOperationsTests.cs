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
    // U = good signature with unknown validity (key in keyring, trust web
    //     hasn't reached it). Maps to UntrustedKey, not UnknownKey.
    [InlineData("U", CommitSignatureStatus.UntrustedKey)]
    [InlineData("X", CommitSignatureStatus.Expired)]
    [InlineData("Y", CommitSignatureStatus.ExpiredKey)]
    [InlineData("R", CommitSignatureStatus.RevokedKey)]
    [InlineData("B", CommitSignatureStatus.Bad)]
    // E = signature cannot be checked, most often because the key isn't
    //     in the local keyring at all. Maps to UnknownKey, not UntrustedKey.
    [InlineData("E", CommitSignatureStatus.UnknownKey)]
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
        // Real %GS shape for GPG: "Name <email>" — splitter pulls the
        // email out of the angle brackets.
        var output = $"abc123{FS}G{FS}Alice <alice@example.com>{FS}ABCD1234EFFE5678{RS}";
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
        var output = $"deadbeef{FS}N{FS}{FS}{RS}";
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
            $"sha1{FS}G{FS}Alice <alice@example.com>{FS}AAA{RS}" +
            $"sha2{FS}U{FS}Bob <bob@example.com>{FS}BBB{RS}" +
            $"sha3{FS}N{FS}{FS}{RS}";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);

        sink.Should().HaveCount(3);
        sink["sha1"].Status.Should().Be(CommitSignatureStatus.Valid);
        sink["sha1"].SignerEmail.Should().Be("alice@example.com");
        sink["sha2"].Status.Should().Be(CommitSignatureStatus.UntrustedKey);
        sink["sha2"].SignerEmail.Should().Be("bob@example.com");
        sink["sha3"].Status.Should().Be(CommitSignatureStatus.None);
    }

    [Fact]
    public void ParseRecords_HandlesTrailingNewlines()
    {
        // git on Windows often emits \r\n between records; the parser
        // trims those off the SHA so the dictionary key is clean.
        var output =
            $"sha1{FS}G{FS}Alice <a@x>{FS}AAA{RS}\r\n" +
            $"sha2{FS}N{FS}{FS}{RS}\r\n";
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
        // Two-field record (missing signer / fingerprint) is invalid;
        // skip silently rather than throwing, since git's output format
        // could change in a future version and we want graceful degradation.
        var output = $"sha1{FS}G{RS}";
        var sink = new Dictionary<string, CommitSignatureData>(StringComparer.OrdinalIgnoreCase);
        CommitSignatureOperations.ParseRecords(output, sink);
        sink.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Alice <alice@example.com>", "Alice", "alice@example.com")]
    [InlineData("Last, First <a@b.com>", "Last, First", "a@b.com")]
    [InlineData("  Padded  <p@d.com>  ", "Padded", "p@d.com")]
    public void SplitSignerNameEmail_GpgFormatSplitsCleanly(string input, string expectedName, string expectedEmail)
    {
        CommitSignatureOperations.SplitSignerNameEmail(input, out var name, out var email);
        name.Should().Be(expectedName);
        email.Should().Be(expectedEmail);
    }

    [Fact]
    public void SplitSignerNameEmail_NoBracketsLeavesEmailEmpty()
    {
        // SSH signatures emit just the key comment with no <email> wrapper.
        // The whole string becomes the name; we don't fabricate an email.
        CommitSignatureOperations.SplitSignerNameEmail("ssh-rsa AAAAB3...", out var name, out var email);
        name.Should().Be("ssh-rsa AAAAB3...");
        email.Should().BeEmpty();
    }

    [Fact]
    public void SplitSignerNameEmail_EmptyInputReturnsEmpty()
    {
        CommitSignatureOperations.SplitSignerNameEmail("", out var name, out var email);
        name.Should().BeEmpty();
        email.Should().BeEmpty();

        CommitSignatureOperations.SplitSignerNameEmail("   ", out name, out email);
        name.Should().BeEmpty();
        email.Should().BeEmpty();
    }
}
