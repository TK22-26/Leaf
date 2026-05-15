#nullable enable
using System.Net;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// Helpers for deciding whether an outbound endpoint URL is safe to
/// send credentials to over plaintext HTTP. The OpenAI-Compatible
/// provider accepts user-typed base URLs — most are legitimate
/// localhost servers (LM Studio default <c>http://localhost:1234</c>)
/// but a typo or paste mistake could send a real API key to a public
/// HTTP host in cleartext. We classify the host so the Settings UI
/// can warn before persisting.
/// </summary>
public static class EndpointSafety
{
    /// <summary>
    /// True when <paramref name="baseUrl"/> is non-empty, parseable as
    /// HTTP (not HTTPS), AND points at a non-loopback / non-private
    /// host. In that case sending an API key is a credential-exposure
    /// risk and the caller should warn.
    /// </summary>
    /// <remarks>
    /// Returns false for HTTPS, loopback (localhost / 127/8 / ::1),
    /// RFC1918 private ranges (10/8, 172.16/12, 192.168/16),
    /// link-local (169.254/16, fe80::/10), and the .local mDNS suffix.
    /// Unparseable URLs return false — they'll fail at request time
    /// with a clearer error, no need to warn here.
    /// </remarks>
    public static bool IsCleartextHttpToPublicHost(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)) return false;
        return !IsPrivateOrLoopback(uri.Host);
    }

    private static bool IsPrivateOrLoopback(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        // Trivial textual matches first — common cases without parsing.
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true; // mDNS

        // IP literal? Parse and classify.
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16 link-local
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // fe80::/10 link-local; ::1 caught by IsLoopback above.
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
            }
            return false;
        }

        // DNS name that didn't match localhost/.local — assume public.
        return false;
    }
}
