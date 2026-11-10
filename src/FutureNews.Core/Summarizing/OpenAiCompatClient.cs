using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FutureNews.Core.Summarizing;

/// <summary>Minimal client for any OpenAI-compatible /chat/completions endpoint.</summary>
public sealed class OpenAiCompatClient
{
    private readonly HttpClient _client;
    private readonly string _model;

    public OpenAiCompatClient(string baseUrl, string model, string apiKey, int timeoutSeconds)
    {
        _model = model;
        _client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 300)),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    /// <summary>Sends a chat completion request and returns the assistant message content.</summary>
    public async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
            temperature = 0.3,
            max_tokens = 1600,
            stream = false,
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("chat/completions", content, ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException($"LLM 返回 {(int)response.StatusCode}: {Truncate(body, 300)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new LlmException("LLM 响应缺少 choices");

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var contentNode) || contentNode.ValueKind != JsonValueKind.String)
            throw new LlmException("LLM 响应缺少 message.content");

        return contentNode.GetString() ?? string.Empty;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

public sealed class LlmException : Exception
{
    public LlmException(string message) : base(message) { }
    public LlmException(string message, Exception inner) : base(message, inner) { }
}
