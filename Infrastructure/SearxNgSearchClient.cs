using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using ResearchApi.Domain; // for HttpUtility.ParseQueryString

namespace ResearchApi.Infrastructure;

public sealed class SearxNgSearchClient : ISearchClient
{
    private readonly HttpClient _httpClient;
    private readonly SearxNgOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public SearxNgSearchClient(HttpClient httpClient, SearxNgOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? languageCode = null,
        string? regionCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        if (limit <= 0)
            limit = 10;

        var baseUrl = _options.BaseUrl?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("SearxNG BaseUrl is not configured.");

        // Determine effective language/region
        var effectiveLang = languageCode ?? _options.DefaultLanguage;
        var effectiveRegion = regionCode ?? _options.DefaultRegion;

        // Some SearxNG setups use "language", some use "locale" like "en_US".
        // Here we:
        // - always send "language" if we have one
        // - send "locale" as "lang_REGION" if both exist (e.g., "en_US", "ja_JP").
        string? locale = null;
        if (!string.IsNullOrWhiteSpace(effectiveLang) && !string.IsNullOrWhiteSpace(effectiveRegion))
        {
            locale = $"{effectiveLang}_{effectiveRegion}";
        }

        var uriBuilder = new UriBuilder($"{baseUrl}/search");
        var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);

        queryParams["q"] = query;
        queryParams["format"] = "json";
        queryParams["engines"] = ""; // optional: leave empty to let SearxNG pick default engines
        queryParams["safesearch"] = _options.SafeSearch.ToString();

        if (!string.IsNullOrWhiteSpace(_options.Categories))
        {
            // categories can be comma-separated if you want multiple (e.g. "general,news")
            queryParams["categories"] = _options.Categories;
        }

        if (!string.IsNullOrWhiteSpace(effectiveLang))
        {
            // Many SearxNG instances accept "language=en", "language=ja", etc.
            queryParams["language"] = effectiveLang;
        }

        if (!string.IsNullOrWhiteSpace(locale))
        {
            // Some setups use "locale" to bias region (e.g., "en_US", "de_DE", "ja_JP")
            queryParams["locale"] = locale;
        }

        // You can also add time_range, etc., here if you want
        // queryParams["time_range"] = "year"; // example

        uriBuilder.Query = queryParams.ToString();
        var requestUri = uriBuilder.Uri;

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SearchResult>();
        }

        var searxResponse = JsonSerializer.Deserialize<SearxNgResponse>(json, _jsonOptions);
        if (searxResponse?.Results == null || searxResponse.Results.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        var results = searxResponse.Results
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Take(limit)
            .Select(r => new SearchResult(
                r.Url!,
                r.Title ?? string.Empty,
                r.Content ?? string.Empty))
            .ToList();

        return results;
    }
}
public sealed class SearxNgResponse
{
    [JsonPropertyName("results")]
    public List<SearxNgResult> Results { get; set; } = new();
}

public sealed class SearxNgResult
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // SearxNG uses "content" for the snippet text
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}