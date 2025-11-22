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
}
