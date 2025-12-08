using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class FirecrawlClient : ISearchClient, ICrawlClient
{
    private readonly HttpClient _httpClient;
    private readonly FirecrawlOptions _options;
    private readonly ILogger<FirecrawlClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public FirecrawlClient(
        HttpClient httpClient,
        IOptions<FirecrawlOptions> options,
        ILogger<FirecrawlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? location = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        // Firecrawl rejects limit <= 0
        limit = Math.Max(1, limit);

        var url = $"{_options.BaseUrl}/v1/search";

        object payload = string.IsNullOrWhiteSpace(location) ?
            new { query, limit } : 
            new { query,
                limit,
                location,
                ignoreInvalidURLs = true
                };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        _logger.LogDebug(
            "Firecrawl search: query='{Query}', limit={Limit}, location='{Location}'",
            query,
            limit,
            location);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Firecrawl search error for query '{Query}': {StatusCode} {ReasonPhrase}. Body: {Body}",
                    query,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    errorText);

                return Array.Empty<SearchResult>();
            }

            var content = await response.Content.ReadAsStringAsync(ct);

            FirecrawlSearchResponse? searchResponse;

            try
            {
                searchResponse = JsonSerializer.Deserialize<FirecrawlSearchResponse>(content, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Failed to deserialize Firecrawl search response for query '{Query}'. Raw: {Raw}",
                    query,
                    content);

                return Array.Empty<SearchResult>();
            }

            if (searchResponse?.data == null || searchResponse.data.Length == 0)
            {
                _logger.LogInformation("Firecrawl search returned no data for query '{Query}'", query);
                return Array.Empty<SearchResult>();
            }

            var results = searchResponse.data
                .Select(d => new SearchResult(d.url, d.title, d.description))
                .ToList();

            _logger.LogInformation(
                "Firecrawl search completed for query '{Query}': {Count} results",
                query,
                results.Count);

            return results;
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

        var endpoint = $"{_options.BaseUrl}/v1/scrape";

        var payload = new
        {
            url,
            timeout = (int)_httpClient.Timeout.TotalMilliseconds,
            formats = new[]
            {
                new { type = "markdown" }
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

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

            _logger.LogInformation(
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

    private sealed class FirecrawlSearchResponse
    {
        public FirecrawlSearchData[]? data { get; set; }
    }

    private sealed class FirecrawlSearchData
    {
        public required string url { get; set; }
        public required string title { get; set; }
        public required string description { get; set; }
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
}