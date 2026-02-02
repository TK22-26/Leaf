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
        catch (HttpRequestException)
        {
            return (false, [], "Ollama is not running");
        }
        catch (TaskCanceledException)
        {
            return (false, [], "Connection timed out");
        }
        catch (Exception ex)
        {
            return (false, [], ex.Message);
        }
    }

    /// <summary>
    /// Generate text using Ollama.
    /// </summary>
    public async Task<(bool success, string output, string? error)> GenerateAsync(
        string baseUrl, string model, string prompt, int timeoutSeconds, CancellationToken ct = default)
    {
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
            return (true, result?.response ?? string.Empty, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, string.Empty, $"Timed out after {timeoutSeconds}s");
        }
        catch (OperationCanceledException)
        {
            return (false, string.Empty, "Cancelled");
        }
        catch (HttpRequestException)
        {
            return (false, string.Empty, "Ollama is not running");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }
}
