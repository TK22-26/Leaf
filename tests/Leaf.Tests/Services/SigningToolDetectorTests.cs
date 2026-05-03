using FluentAssertions;
using Leaf.Services.Signing;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Pure-logic tests for <see cref="SigningToolDetector"/>: the gpg
/// --with-colons output parser. Tool-detection itself spawns a real
/// process and lives in the integration suite.
/// </summary>
public class SigningToolDetectorTests
{
    [Fact]
    public void ParseGpgColonOutput_SingleKey()
    {
        // Minimal real-world gpg --with-colons output. Field positions
        // matter: sec[4]=keyid, fpr[9]=fingerprint, uid[9]=user id.
        var output =
            "sec:u:4096:1:ABCDEF1234567890:1700000000:::u:::scESCa:::+:::23::0:\n" +
            "fpr:::::::::AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555:\n" +
            "uid:u::::1700000000::DEADBEEF::Alice <alice@example.com>::::::::::0:\n";

        var keys = SigningToolDetector.ParseGpgColonOutput(output);
        keys.Should().HaveCount(1);
        keys[0].LongKeyId.Should().Be("ABCDEF1234567890");
        keys[0].Fingerprint.Should().Be("AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555");
        keys[0].PrimaryUid.Should().Be("Alice <alice@example.com>");
    }

    [Fact]
    public void ParseGpgColonOutput_MultipleKeys()
    {
        var output =
            "sec:u:4096:1:KEY1:1700000000::::::scESCa:::+:::23::0:\n" +
            "fpr:::::::::FPR1:\n" +
            "uid:u::::1700000000::DEADBEEF::Alice::::::::::0:\n" +
            "sec:u:4096:1:KEY2:1700000001::::::scESCa:::+:::23::0:\n" +
            "fpr:::::::::FPR2:\n" +
            "uid:u::::1700000001::ABADCAFE::Bob::::::::::0:\n";

        var keys = SigningToolDetector.ParseGpgColonOutput(output);
        keys.Should().HaveCount(2);
        keys[0].LongKeyId.Should().Be("KEY1");
        keys[1].LongKeyId.Should().Be("KEY2");
    }

    [Fact]
    public void ParseGpgColonOutput_UnescapesColonInUid()
    {
        // A real example: a user id containing a colon ends up escaped
        // in the colon-separated format. Round-trip cleanly back to the
        // human-readable form.
        var output =
            "sec:u:4096:1:KEY1:1700000000::::::scESCa:::+:::23::0:\n" +
            "uid:u::::1700000000::ABC::User <user\\x3aname@host>::::::::::0:\n";

        var keys = SigningToolDetector.ParseGpgColonOutput(output);
        keys.Should().HaveCount(1);
        keys[0].PrimaryUid.Should().Be("User <user:name@host>");
    }

    [Fact]
    public void ParseGpgColonOutput_KeyWithoutUidStillReturnsKey()
    {
        // Real keys always have at least one uid, but a malformed key
        // shouldn't crash the parser — we want to surface the row so the
        // user can pick it (or notice it's broken).
        var output = "sec:u:4096:1:KEYNOPRINTS:1700000000::::::scESCa:::+:::23::0:\n";
        var keys = SigningToolDetector.ParseGpgColonOutput(output);
        keys.Should().HaveCount(1);
        keys[0].LongKeyId.Should().Be("KEYNOPRINTS");
        keys[0].PrimaryUid.Should().Be("(no user id)");
    }

    [Fact]
    public void ParseGpgColonOutput_EmptyInputReturnsEmpty()
    {
        SigningToolDetector.ParseGpgColonOutput(string.Empty).Should().BeEmpty();
        SigningToolDetector.ParseGpgColonOutput("\n").Should().BeEmpty();
    }
}
