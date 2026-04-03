using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ResearchEngine.Configuration;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public class FirecrawlClient : ISearchClient, ICrawlClient
{
    private readonly HttpClient _httpClient;
    private readonly IRuntimeSettingsAccessor _runtimeSettings;
    private readonly ILogger<FirecrawlClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FirecrawlClient(
        HttpClient httpClient,
        IRuntimeSettingsAccessor runtimeSettings,
        ILogger<FirecrawlClient> logger)
    {
        _httpClient = httpClient;
        _runtimeSettings = runtimeSettings;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest requestModel,
        CancellationToken ct = default)
    {
        var query = requestModel.Query;
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        // Firecrawl rejects limit <= 0
        var limit = Math.Max(1, requestModel.Limit);

        var crawlConfig = await _runtimeSettings.GetCurrentAsync(ct);
        var baseUrl = crawlConfig.CrawlConfig.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("CrawlConfig.BaseUrl is not configured.");
            return Array.Empty<SearchResult>();
        }

        var payload = CreateSearchPayload(requestModel, limit);

        _logger.LogDebug(
            "Firecrawl search: query='{Query}', limit={Limit}, location='{Location}', discoveryMode='{DiscoveryMode}'",
            query,
            limit,
            requestModel.Location,
            requestModel.DiscoveryMode);

        try
        {
            var v2Results = await SearchCoreAsync(
                $"{baseUrl.TrimEnd('/')}/v2/search",
                payload,
                crawlConfig.CrawlConfig.ApiKey,
                query,
                ct);

            if (v2Results.Success)
                return v2Results.Results;

            _logger.LogInformation(
                "Falling back to Firecrawl v1 search for query '{Query}' after v2 search was unavailable or incompatible.",
                query);

            var v1Results = await SearchCoreAsync(
                $"{baseUrl.TrimEnd('/')}/v1/search",
                CreateLegacySearchPayload(requestModel, limit),
                crawlConfig.CrawlConfig.ApiKey,
                query,
                ct);

            return v1Results.Results;
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(oce,
                "Firecrawl search request timed out for query '{Query}'",
                query);
            return Array.Empty<SearchResult>();
        }
        catch (HttpRequestException hre)
        {
            _logger.LogWarning(hre,
                "Firecrawl search request failed for query '{Query}'",
                query);
            return Array.Empty<SearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during Firecrawl search for query '{Query}'",
                query);
            return Array.Empty<SearchResult>();
        }
    }

    public async Task<string> FetchContentAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var crawlConfig = await _runtimeSettings.GetCurrentAsync(ct);
        var baseUrl = crawlConfig.CrawlConfig.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("CrawlConfig.BaseUrl is not configured.");
            return string.Empty;
        }

        var endpoint = $"{baseUrl.TrimEnd('/')}/v1/scrape";

        var payload = new
        {
            url,
            timeout = (int)_httpClient.Timeout.TotalMilliseconds,
            formats = new[] { "markdown" }
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        AddApiKeyHeaders(request, crawlConfig.CrawlConfig.ApiKey);

        _logger.LogDebug("Firecrawl scrape: url={Url}", url);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Firecrawl scrape error for URL {Url}: {StatusCode} {ReasonPhrase}. Body: {ErrorText}",
                    url,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    errorText);

                return string.Empty;
            }

            var content = await response.Content.ReadAsStringAsync(ct);

            FirecrawlScrapeResponse? scrapeResponse;
            try
            {
                scrapeResponse = JsonSerializer.Deserialize<FirecrawlScrapeResponse>(content, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Failed to deserialize Firecrawl scrape response for URL {Url}. Raw: {Raw}",
                    url,
                    content);
                return string.Empty;
            }

            var markdown = scrapeResponse?.data?.markdown ?? string.Empty;

            _logger.LogDebug(
                "Fetched markdown content from URL {Url} with length {Length}",
                url,
                markdown.Length);

            return markdown;
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(oce,
                "Firecrawl scrape request timed out for URL {Url}",
                url);
            return string.Empty;
        }
        catch (HttpRequestException hre)
        {
            _logger.LogWarning(hre,
                "Firecrawl scrape request failed for URL {Url}",
                url);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during Firecrawl scrape for URL {Url}",
                url);
            return string.Empty;
        }
    }

    private sealed class FirecrawlScrapeResponse
    {
        public bool success { get; set; }
        public FirecrawlScrapeData? data { get; set; }
    }

    private sealed class FirecrawlScrapeData
    {
        public string? markdown { get; set; }
    }

    private async Task<(bool Success, IReadOnlyList<SearchResult> Results)> SearchCoreAsync(
        string url,
        object payload,
        string? apiKey,
        string query,
        CancellationToken ct)
    {
        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        AddApiKeyHeaders(request, apiKey);

        using var response = await _httpClient.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Firecrawl search error for query '{Query}': {StatusCode} {ReasonPhrase}. Body: {Body}",
                query,
                (int)response.StatusCode,
                response.ReasonPhrase,
                content);

            return (false, Array.Empty<SearchResult>());
        }

        if (!TryParseSearchResults(content, out var results))
        {
            _logger.LogWarning(
                "Firecrawl search returned an unexpected payload for query '{Query}'. Raw: {Raw}",
                query,
                content);

            return (false, Array.Empty<SearchResult>());
        }

        _logger.LogDebug(
            "Firecrawl search completed for query '{Query}': {Count} results",
            query,
            results.Count);

        return (true, results);
    }

    private object CreateSearchPayload(SearchRequest requestModel, int limit)
    {
        var payload = new Dictionary<string, object?>
        {
            ["query"] = requestModel.Query,
            ["limit"] = limit,
            ["sources"] = new[] { "web" },
            ["ignoreInvalidURLs"] = true
        };

        if (!string.IsNullOrWhiteSpace(requestModel.Location))
            payload["location"] = requestModel.Location;

        if (requestModel.DiscoveryMode == SourceDiscoveryMode.AcademicOnly)
            payload["categories"] = new[] { "research", "pdf" };

        return payload;
    }

    private object CreateLegacySearchPayload(SearchRequest requestModel, int limit)
        => string.IsNullOrWhiteSpace(requestModel.Location)
            ? new
            {
                query = requestModel.Query,
                limit,
                ignoreInvalidURLs = true
            }
            : new
            {
                query = requestModel.Query,
                limit,
                location = requestModel.Location,
                ignoreInvalidURLs = true
            };

    private bool TryParseSearchResults(string content, out IReadOnlyList<SearchResult> results)
    {
        results = Array.Empty<SearchResult>();

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("data", out var data))
                return false;

            if (data.ValueKind == JsonValueKind.Array)
            {
                results = data.EnumerateArray()
                    .Select(ToSearchResult)
                    .Where(static x => !string.IsNullOrWhiteSpace(x.Url))
                    .ToList();
                return true;
            }

            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("web", out var web) && web.ValueKind == JsonValueKind.Array)
            {
                results = web.EnumerateArray()
                    .Select(ToSearchResult)
                    .Where(static x => !string.IsNullOrWhiteSpace(x.Url))
                    .ToList();
                return true;
            }

            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Firecrawl search response.");
            return false;
        }
    }

    private static SearchResult ToSearchResult(JsonElement item)
    {
        var url = GetString(item, "url") ?? string.Empty;
        var title = GetString(item, "title") ?? string.Empty;
        var description = GetString(item, "description")
            ?? GetString(item, "snippet")
            ?? string.Empty;
        var category = GetString(item, "category");
        var position = item.TryGetProperty("position", out var positionEl) && positionEl.ValueKind == JsonValueKind.Number
            ? positionEl.GetInt32()
            : (int?)null;
        var publishedDate = GetString(item, "date");
        var domain = NormalizeDomain(url);

        return new SearchResult(url, title, description, domain, category, position, publishedDate);
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NormalizeDomain(string? rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.Trim().ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static void AddApiKeyHeaders(HttpRequestMessage request, string? apiKey)
    {
        var normalizedApiKey = apiKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedApiKey))
            return;

        // Firecrawl API commonly uses bearer auth; also send x-api-key for compatibility.
        if (normalizedApiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("Authorization", normalizedApiKey);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedApiKey);
        }

        request.Headers.TryAddWithoutValidation("x-api-key", normalizedApiKey);
    }
}
