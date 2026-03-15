using System.Text.Json;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class VllmTokenizer : TokenizerBase, IDisposable
{
    private readonly HttpClient _httpClient;

    public VllmTokenizer(
        IRuntimeSettingsAccessor runtimeSettings,
        HttpClient? httpClient = null)
        : base(runtimeSettings)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    protected override async Task<TokenizeResult> TokenizeCoreAsync(
        ChatConfig config,
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (payload is null) throw new ArgumentNullException(nameof(payload));

        var uri = OpenAiEndpointUri.AppendServerPath(config.Endpoint, "tokenize");

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
