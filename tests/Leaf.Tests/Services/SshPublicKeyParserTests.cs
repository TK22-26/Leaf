using FluentAssertions;
using Leaf.Services.Ssh;
using Xunit;

namespace Leaf.Tests.Services;

/// <summary>
/// Tests for <see cref="SshPublicKeyParser"/> — the only piece of §5.13
/// public-key handling that doesn't talk to ssh-keygen, so the only
/// piece worth a unit test in isolation. The fingerprint pipeline is
/// covered by integration testing.
/// </summary>
public class SshPublicKeyParserTests
{
    [Theory]
    [InlineData("ssh-ed25519 AAAA... user@host", SshKeyAlgorithm.Ed25519, "user@host")]
    [InlineData("ssh-rsa AAAAB3... someone@example.com", SshKeyAlgorithm.Rsa, "someone@example.com")]
    [InlineData("ecdsa-sha2-nistp256 AAAA... key with spaces", SshKeyAlgorithm.Ecdsa, "key with spaces")]
    [InlineData("ssh-dss AAAA... legacy", SshKeyAlgorithm.Dsa, "legacy")]
    public void TryParse_KnownAlgorithms_ReturnsAlgorithmAndComment(string text, SshKeyAlgorithm expected, string expectedComment)
    {
        SshPublicKeyParser.TryParse(text, out var algorithm, out var comment).Should().BeTrue();
        algorithm.Should().Be(expected);
        comment.Should().Be(expectedComment);
    }

    [Fact]
    public void TryParse_NoCommentField_ReturnsEmptyComment()
    {
        SshPublicKeyParser.TryParse("ssh-ed25519 AAAA", out var algorithm, out var comment).Should().BeTrue();
        algorithm.Should().Be(SshKeyAlgorithm.Ed25519);
        comment.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_TrailingNewline_IsStripped()
    {
        SshPublicKeyParser.TryParse("ssh-ed25519 AAAA me\n", out var algorithm, out var comment).Should().BeTrue();
        algorithm.Should().Be(SshKeyAlgorithm.Ed25519);
        comment.Should().Be("me");
    }

    [Fact]
    public void TryParse_UnknownAlgorithm_ReturnsFalse()
    {
        SshPublicKeyParser.TryParse("ssh-future AAAA me", out var algorithm, out var comment).Should().BeFalse();
        algorithm.Should().Be(SshKeyAlgorithm.Unknown);
        comment.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# comment-only file")]
    [InlineData("ssh-ed25519")] // missing key blob
    public void TryParse_Garbage_ReturnsFalse(string text)
    {
        SshPublicKeyParser.TryParse(text, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseFingerprintLine_StandardOutput_ReadsBitsFingerprintAndComment()
    {
        var line = "256 SHA256:abcdefg user@host (ED25519)";
        SshPublicKeyParser.TryParseFingerprintLine(line, out var bits, out var fp, out var comment).Should().BeTrue();
        bits.Should().Be(256);
        fp.Should().Be("SHA256:abcdefg");
        comment.Should().Be("user@host");
    }

    [Fact]
    public void TryParseFingerprintLine_NoTrailingType_StillParses()
    {
        var line = "4096 SHA256:xyz me@example.com";
        SshPublicKeyParser.TryParseFingerprintLine(line, out var bits, out var fp, out var comment).Should().BeTrue();
        bits.Should().Be(4096);
        fp.Should().Be("SHA256:xyz");
        comment.Should().Be("me@example.com");
    }

    [Fact]
    public void TryParseFingerprintLine_BlankLine_ReturnsFalse()
    {
        SshPublicKeyParser.TryParseFingerprintLine(string.Empty, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void MapAlgorithm_HandlesCertificateVariants()
    {
        SshPublicKeyParser.MapAlgorithm("ssh-ed25519-cert-v01@openssh.com").Should().Be(SshKeyAlgorithm.Ed25519);
        SshPublicKeyParser.MapAlgorithm("ssh-rsa-cert-v01@openssh.com").Should().Be(SshKeyAlgorithm.Rsa);
        SshPublicKeyParser.MapAlgorithm("ecdsa-sha2-nistp256-cert-v01@openssh.com").Should().Be(SshKeyAlgorithm.Ecdsa);
    }

    [Theory]
    [InlineData("ED25519", SshKeyAlgorithm.Ed25519)]
    [InlineData("RSA", SshKeyAlgorithm.Rsa)]
    [InlineData("ECDSA", SshKeyAlgorithm.Ecdsa)]
    [InlineData("DSA", SshKeyAlgorithm.Dsa)]
    public void MapAlgorithm_HandlesAgentShortForms(string token, SshKeyAlgorithm expected)
    {
        // ssh-add appends the upper-case algorithm in parentheses on
        // each fingerprint line — `4096 SHA256:abc me@host (RSA)`. The
        // mapper must recognise both these short forms and the long-
        // form tokens .pub files use; this is the regression net for
        // that union.
        SshPublicKeyParser.MapAlgorithm(token).Should().Be(expected);
    }

    [Fact]
    public void MapAlgorithm_RejectsLowercaseShortForms()
    {
        // ssh-add only emits upper-case; lowercase shouldn't be
        // accidentally accepted because it would mask a future
        // regression where the agent output format changed.
        SshPublicKeyParser.MapAlgorithm("ed25519").Should().Be(SshKeyAlgorithm.Unknown);
        SshPublicKeyParser.MapAlgorithm("rsa").Should().Be(SshKeyAlgorithm.Unknown);
    }
}
