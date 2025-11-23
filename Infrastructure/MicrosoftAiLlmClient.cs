using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public class MicrosoftAiLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MicrosoftAiLlmClient> _logger;

    private readonly LlmOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MicrosoftAiLlmClient(HttpClient httpClient, IOptions<LlmOptions> options, ILogger<MicrosoftAiLlmClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.Endpoint) ||
            string.IsNullOrWhiteSpace(_options.Model))
        {
            throw new InvalidOperationException("LLM options are not fully configured.");
        }
    }

    public async Task<string> CompleteAsync(Prompt prompt, CancellationToken cancellationToken = default)
    {
        var url = $"{_options.Endpoint.TrimEnd('/')}/chat/completions";

        var payload = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = prompt.systemPrompt },
                new { role = "user",   content = prompt.userPrompt }
            }
            // you can add max_tokens, temperature, etc. here if you want
        };

        var jsonPayload = JsonSerializer.Serialize(payload, JsonOptions);

        _logger.LogDebug("LLM Prompt:\n{Prompt}", prompt.userPrompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        // For OpenAI-compatible servers, this is usually fine
        // request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        var completion = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(content, JsonOptions)
                        ?? throw new InvalidOperationException("Failed to deserialize LLM response.");

        var text = completion.Choices
            ?.LastOrDefault()
            ?.Message
            ?.Content
            ?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("LLM returned an empty or invalid completion.");
        }

        _logger.LogDebug("LLM Raw Output:\n{Output}", text);

        return text;
    }

    /// <summary>
    /// Counts tokens for a Prompt using vLLM /tokenize with chat-style messages.
    /// </summary>
    public async Task<int> CountTokensAsync(Prompt prompt, CancellationToken ct = default)
    {
        if (prompt == null)
            return 0;

        var system = prompt.systemPrompt?.Trim();
        var user   = prompt.userPrompt?.Trim();

        // If nothing to send – zero tokens
        if (string.IsNullOrEmpty(system) && string.IsNullOrEmpty(user))
            return 0;

        var requestUri = $"{_options.Endpoint.TrimEnd('/')}/tokenize";

        // Build messages array matching what you send to /v1/chat/completions
        var messages = new List<object>();

        if (!string.IsNullOrEmpty(system))
        {
            messages.Add(new
            {
                role = "system",
                content = system
            });
        }

        if (!string.IsNullOrEmpty(user))
        {
            messages.Add(new
            {
                role = "user",
                content = user
            });
        }

        var payload = new
        {
            model = _options.Model, 
            messages
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        _logger.LogDebug(
            "Calling vLLM tokenizer at {Uri} with {MessageCount} messages (system={HasSystem}, user={HasUser})",
            requestUri,
            messages.Count,
            !string.IsNullOrEmpty(system),
            !string.IsNullOrEmpty(user)
        );

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "vLLM tokenizer (messages) failed: {StatusCode} {Reason}. Body: {Body}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                errorBody
            );

            // Heuristic fallback: ~4 chars per token on combined text
            var combinedLen = (system?.Length ?? 0) + (user?.Length ?? 0);
            return Math.Max(1, combinedLen / 4);
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);

        VllmTokenizeResponse? tokenizeResponse;
        try
        {
            tokenizeResponse = JsonSerializer.Deserialize<VllmTokenizeResponse>(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize vLLM tokenizer (messages) response: {Json}", responseJson);
            var combinedLen = (system?.Length ?? 0) + (user?.Length ?? 0);
            return Math.Max(1, combinedLen / 4);
        }

        if (tokenizeResponse == null)
        {
            _logger.LogWarning("vLLM tokenizer (messages) returned null response, falling back to heuristic");
            var combinedLen = (system?.Length ?? 0) + (user?.Length ?? 0);
            return Math.Max(1, combinedLen / 4);
        }

        _logger.LogDebug(
            "vLLM tokenizer (messages): count={Count}, max_model_len={MaxLen}",
            tokenizeResponse.count,
            tokenizeResponse.max_model_len
        );

        return tokenizeResponse.count;
    }

    public string StripThinkBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove <think>...</think> blocks (non-greedy, multiline)
        var withoutThink = Regex.Replace(
            text,
            @"<think>.*?</think>",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return withoutThink.Trim();
    }

    // DTOs for the OpenAI-style response
    private sealed class OpenAiChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = [];
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public Message Message { get; set; } = default!;
    }

    private sealed class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class VllmTokenizeResponse
    {
        public int count { get; set; }
        public int max_model_len { get; set; }
        public int[]? tokens { get; set; }
    }
}
