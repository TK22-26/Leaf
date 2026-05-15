#nullable enable
using System.Net;
using System.Net.Http;
using Leaf.Services.Merge;

namespace Leaf.Services.Ai.Http;

/// <summary>
/// Shared scaffolding for every <see cref="IAiApiClient"/>. Owns the
/// HttpClient, the cached API key, the 401/429 surfacing, and the
/// timeout wiring — derived classes only build the request body, set
/// the auth header, and unwrap the provider's response envelope.
/// </summary>
/// <remarks>
/// HttpClient is supplied by DI and shared across providers — auth is
/// set per-request (never on <c>DefaultRequestHeaders</c>) so we don't
/// accidentally leak one provider's key onto another's request.
/// </remarks>
public abstract class AiApiClientBase : IAiApiClient
{
    /// <summary>
    /// Upper bound on the response body we'll buffer. 4 MB is orders
    /// of magnitude above any sane structured-output response and
    /// keeps a malicious or misbehaving endpoint from OOM'ing the
    /// process by streaming gigabytes of bytes. Mirrors the
    /// <see cref="AiResolutionParser"/>'s 256 KB downstream cap with
    /// extra headroom for envelope/tool-use overhead.
    /// </summary>
    private const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly Func<string?> _keyReader;
    private readonly Func<int> _timeoutSecondsProvider;
    private string? _cachedKey;
    private readonly object _keyLock = new();

    protected AiApiClientBase(
        HttpClient httpClient,
        Func<string?> keyReader,
        Func<int> timeoutSecondsProvider)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _keyReader = keyReader ?? throw new ArgumentNullException(nameof(keyReader));
        _timeoutSecondsProvider = timeoutSecondsProvider ?? throw new ArgumentNullException(nameof(timeoutSecondsProvider));
    }

    public abstract AiProviderKind Provider { get; }

    /// <summary>Human-readable name for error messages: "Claude (API)" etc.</summary>
    protected abstract string ProviderLabel { get; }

    public bool HasKey => !string.IsNullOrEmpty(GetKey());

    public void RefreshKey()
    {
        lock (_keyLock) _cachedKey = null;
    }

    /// <summary>
    /// Cached key access. The credential read goes through DPAPI under
    /// the hood — re-reading on every merge click is wasteful, so we
    /// cache for the lifetime of the singleton and invalidate via
    /// <see cref="RefreshKey"/> when the Settings UI changes it.
    /// </summary>
    protected string? GetKey()
    {
        lock (_keyLock)
        {
            if (_cachedKey is null)
            {
                _cachedKey = _keyReader();
            }
            return string.IsNullOrEmpty(_cachedKey) ? null : _cachedKey;
        }
    }

    public async Task<string> SendAsync(string prompt, string jsonSchema, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(jsonSchema);

        var key = GetKey()
            ?? throw new AiMergeAssistantException(
                $"{ProviderLabel}: no API key configured. Open Settings → AI to set one.");

        using var request = BuildRequest(prompt, jsonSchema, key);
        var rawBody = await SendAndReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        return ExtractStructuredOutput(rawBody);
    }

    public async Task<string?> TestConnectionAsync(CancellationToken cancellationToken)
    {
        var key = GetKey();
        if (string.IsNullOrEmpty(key)) return "No API key configured.";
        try
        {
            using var request = BuildTestRequest(key);
            _ = await SendAndReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (AiMergeAssistantException ex)
        {
            return ex.Message;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var key = GetKey()
            ?? throw new AiMergeAssistantException(
                $"{ProviderLabel}: no API key configured. Open Settings → AI to set one.");

        using var request = BuildListModelsRequest(key);
        var rawBody = await SendAndReadBodyAsync(request, cancellationToken).ConfigureAwait(false);
        return ParseModels(rawBody);
    }

    /// <summary>
    /// Build the merge-resolution HTTP request. Derived classes set the
    /// URL, auth header, content-type, and serialize the provider-specific
    /// request body.
    /// </summary>
    protected abstract HttpRequestMessage BuildRequest(string prompt, string jsonSchema, string apiKey);

    /// <summary>
    /// Build a minimal request used by <see cref="TestConnectionAsync"/>.
    /// Default implementation reuses <see cref="BuildRequest"/> with a
    /// 1-token ping payload — providers can override for cheaper probes.
    /// </summary>
    protected virtual HttpRequestMessage BuildTestRequest(string apiKey)
        => BuildRequest("ping", "{\"type\":\"object\"}", apiKey);

    /// <summary>
    /// Parse the provider's response envelope and return the inner JSON
    /// object string. The downstream <see cref="AiResolutionParser"/>
    /// expects exactly the resolution JSON — strip everything else here.
    /// </summary>
    protected abstract string ExtractStructuredOutput(string rawBody);

    /// <summary>
    /// Build the GET request for the provider's models-list endpoint.
    /// Derived classes set the URL and the auth header in exactly the
    /// same way as <see cref="BuildRequest"/>. No request body.
    /// </summary>
    protected abstract HttpRequestMessage BuildListModelsRequest(string apiKey);

    /// <summary>
    /// Parse the models-list response into a list of model identifiers
    /// suitable for the Settings dropdown. Each provider has a different
    /// envelope shape — Anthropic <c>data[].id</c>, Google <c>models[].name</c>
    /// (prefixed <c>models/</c>), OpenAI <c>data[].id</c> — and a
    /// different filter rule (chat-capable only). Implementations should
    /// return identifiers in a sensible recommendation order.
    /// </summary>
    protected abstract IReadOnlyList<string> ParseModels(string rawBody);

    /// <summary>
    /// Shared HTTP pipeline: timeout, response-size cap, error mapping.
    /// Returns the success body as a string for callers (SendAsync,
    /// TestConnectionAsync, ListModelsAsync) to interpret as they see
    /// fit. Renamed from the old <c>ExecuteAsync</c> when ListModelsAsync
    /// landed and the per-call response-shape logic split from the
    /// transport-shape logic.
    /// </summary>
    private async Task<string> SendAndReadBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var timeoutSeconds = Math.Max(1, _timeoutSecondsProvider());
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead lets us inspect Content-Length and
            // stream-read the body with a hard cap, rather than letting
            // HttpClient buffer the entire payload into memory before
            // we get a say.
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiMergeAssistantException($"{ProviderLabel}: request timed out after {timeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiMergeAssistantException($"{ProviderLabel}: network error ({ex.Message}).", ex);
        }

        using (response)
        {
            // Reject obviously oversized responses up front using
            // Content-Length when present — saves reading any body
            // bytes at all on a hostile or misconfigured server.
            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength.HasValue && declaredLength.Value > MaxResponseBytes)
            {
                throw new AiMergeAssistantException(
                    $"{ProviderLabel}: response too large ({declaredLength.Value} bytes; cap {MaxResponseBytes}).");
            }

            var body = await ReadBodyCappedAsync(response, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiMergeAssistantException(BuildHttpErrorMessage(response.StatusCode, body));
            }
            return body;
        }
    }

    private async Task<string> ReadBodyCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Read up to MaxResponseBytes + 1 — if the extra byte arrives,
        // we know the body exceeded the cap without having to buffer
        // gigabytes to find out.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[8192];
        var output = new System.IO.MemoryStream(capacity: 8192);
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaxResponseBytes)
            {
                throw new AiMergeAssistantException(
                    $"{ProviderLabel}: response exceeded {MaxResponseBytes} byte cap.");
            }
            output.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private string BuildHttpErrorMessage(HttpStatusCode status, string body)
    {
        // Scrub any API-key shapes in the body before embedding. Real
        // providers don't echo keys back, but a misconfigured corporate
        // proxy or a custom OpenAI-compatible server might — and once
        // that string is inside an exception message it can land in
        // logs, telemetry, or a UI toast.
        var compact = Leaf.Services.Log.Redact(body).Replace("\r", " ").Replace("\n", " ");
        if (compact.Length > 240) compact = compact[..240] + "…";
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{ProviderLabel}: API key rejected ({(int)status}). Update it in Settings → AI.",
            (HttpStatusCode)429 =>
                $"{ProviderLabel}: rate limited (429). Retry shortly.",
            _ => $"{ProviderLabel}: HTTP {(int)status} {status} — {compact}",
        };
    }
}
