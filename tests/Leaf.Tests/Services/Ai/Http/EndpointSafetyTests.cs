#nullable enable
using FluentAssertions;
using Leaf.Services.Ai.Http;
using Xunit;

namespace Leaf.Tests.Services.Ai.Http;

/// <summary>
/// Classification of base URLs as cleartext-HTTP-to-public-host
/// (where sending an API key would be a credential exposure) vs
/// safe (HTTPS, loopback, private ranges, mDNS). Drives the Save & Test
/// warning dialog in <c>AiSettingsControl</c>.
/// </summary>
public class EndpointSafetyTests
{
    [Theory]
    // HTTPS — always safe regardless of host.
    [InlineData("https://api.openai.com/v1")]
    [InlineData("https://openrouter.ai/api/v1")]
    [InlineData("https://localhost:1234/v1")]
    // HTTP to loopback / private ranges — safe.
    [InlineData("http://localhost:1234/v1")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:1234")]
    [InlineData("http://10.0.5.20:9000")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.254")]
    [InlineData("http://192.168.1.100/v1")]
    [InlineData("http://169.254.1.1")]
    [InlineData("http://mac-mini.local:1234")]
    // Empty / null — no warning (separate guard catches it).
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    // Unparseable — no warning (will fail at request time with a
    // clearer error).
    [InlineData("not-a-url")]
    public void Safe_ReturnsFalse(string? url)
    {
        EndpointSafety.IsCleartextHttpToPublicHost(url).Should().BeFalse();
    }

    [Theory]
    // HTTP to public DNS / public IP — warn.
    [InlineData("http://api.openai.com/v1")]
    [InlineData("http://openrouter.ai/api/v1")]
    [InlineData("http://attacker.example.com:8080/v1")]
    [InlineData("http://1.1.1.1")]
    [InlineData("http://172.32.0.1")]   // just outside private 172.16/12
    [InlineData("http://172.15.0.1")]   // just outside on the low side
    [InlineData("http://11.0.0.1")]     // just outside 10/8
    [InlineData("http://192.169.0.1")]  // just outside 192.168/16
    public void UnsafePublicHttp_ReturnsTrue(string url)
    {
        EndpointSafety.IsCleartextHttpToPublicHost(url).Should().BeTrue();
    }
}
