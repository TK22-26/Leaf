using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;

namespace Leaf.Services;

/// <summary>
/// Service for interacting with Ollama local LLM via REST API.
/// </summary>
public class OllamaService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    // DTOs for Ollama JSON parsing
    private class OllamaTagsResponse
    {
        public List<OllamaModel>? models { get; set; }
    }

    private class OllamaModel
    {
        public string name { get; set; } = string.Empty;
    }

    private class OllamaGenerateRequest
    {
        public string model { get; set; } = string.Empty;
        public string prompt { get; set; } = string.Empty;
        public bool stream { get; set; }
    }

    private class OllamaGenerateResponse
    {
        public string response { get; set; } = string.Empty;
    }

    /// <summary>
    /// Get available models from Ollama.
    /// </summary>
    public async Task<(bool success, List<string> models, string? error)> GetAvailableModelsAsync(string baseUrl)
    {
        try
        {
            var url = $"{baseUrl.TrimEnd('/')}/api/tags";
            var response = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>(url);
            var modelNames = response?.models?.Select(m => m.name).ToList() ?? [];
            return (modelNames.Count > 0, modelNames, modelNames.Count == 0 ? "No models installed" : null);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Ollama", "Failed to get models - Ollama not running", ex);
            return (false, [], "Ollama is not running");
        }
        catch (TaskCanceledException ex)
        {
            Log.Error("Ollama", "Failed to get models - connection timed out", ex);
            return (false, [], "Connection timed out");
        }
        catch (Exception ex)
        {
            Log.Error("Ollama", "Failed to get models", ex);
            return (false, [], ex.Message);
        }
    }

    /// <summary>
    /// Generate text using Ollama.
    /// </summary>
    public async Task<(bool success, string output, string? error)> GenerateAsync(
        string baseUrl, string model, string prompt, int timeoutSeconds, CancellationToken ct = default)
    {
        Log.Info("Ollama", $"Generate request: model={model}, timeout={timeoutSeconds}s, prompt length={prompt.Length}");
        var sw = Log.StartTimer();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var url = $"{baseUrl.TrimEnd('/')}/api/generate";
            var request = new OllamaGenerateRequest
            {
                model = model,
                prompt = prompt,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(url, request, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return (false, string.Empty, $"Ollama Error: {response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cts.Token);
            Log.Perf("Ollama", $"Generate complete: model={model}", sw.ElapsedMilliseconds);
            return (true, result?.response ?? string.Empty, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Error("Ollama", $"Generate timed out after {timeoutSeconds}s for model={model}");
            return (false, string.Empty, $"Timed out after {timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            return (false, string.Empty, "Cancelled");
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Ollama", "Generate failed - Ollama not running", ex);
            return (false, string.Empty, "Ollama is not running");
        }
        catch (Exception ex)
        {
            Log.Error("Ollama", "Generate failed", ex);
            return (false, string.Empty, ex.Message);
        }
    }
}
