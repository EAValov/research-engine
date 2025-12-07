using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearchApi.Domain;
using ResearchApi.Prompts;

namespace ResearchApi.Infrastructure;

public sealed class VllmTokenizer : ITokenizer, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly TokenizerConfig _config;
    private readonly Uri _baseUri;

    public VllmTokenizer(
        IOptions<TokenizerConfig> options,
        HttpClient? httpClient = null)
    {
        _config = options.Value ?? throw new ArgumentNullException(nameof(options));

        _baseUri = new Uri(_config.BaseUrl, UriKind.Absolute);
        _httpClient = httpClient ?? new HttpClient();
    }

      public Task<TokenizeResult> TokenizePromptAsync(
        Prompt prompt,
        CancellationToken cancellationToken = default)
    {
        if (prompt is null) throw new ArgumentNullException(nameof(prompt));
        if (prompt.systemPrompt is null)
            throw new ArgumentNullException(nameof(prompt.systemPrompt));
        if (prompt.userPrompt is null)
            throw new ArgumentNullException(nameof(prompt.userPrompt));

        var model = _config.ModelId
                    ?? throw new InvalidOperationException(
                        "TokenizerConfig.ModelId must be set to use TokenizePromptAsync.");

        var messages = new List<object>
        {
            new { role = "system", content = prompt.systemPrompt },
            new { role = "user",   content = prompt.userPrompt }
        };

        var payload = new
        {
            model,
            messages
        };

        return TokenizeAsync(payload, cancellationToken);
    }

    async Task<TokenizeResult> TokenizeAsync(
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));

        var uri = new Uri(_baseUri, "tokenize");

        using var resp = await _httpClient.PostAsJsonAsync(
                uri,
                payload,
                cancellationToken)
            .ConfigureAwait(false);

        var rawJson = await resp.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Tokenize failed: HTTP {(int)resp.StatusCode} {resp.StatusCode}, body: {rawJson}");
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var parsed = JsonSerializer.Deserialize<TokenizeResult>(rawJson, options)
                    ?? throw new InvalidOperationException("Failed to deserialize /tokenize response.");

        return parsed;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
