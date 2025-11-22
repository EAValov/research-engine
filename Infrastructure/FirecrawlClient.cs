using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using ResearchApi.Configuration;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options, ILogger<FirecrawlClient> logger)
    : ISearchClient, ICrawlClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly FirecrawlOptions _options = options.Value;
    private readonly ILogger<FirecrawlClient> _logger = logger;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? location = null,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v1/search");
        
        var payload = new {
                query,
                limit,
                location,
                ignoreInvalidURLs = true
            };

        var jsonPayload = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        _logger.LogDebug("Executing search for query '{Query}' with limit {Limit}, location '{Location}'", query, limit, location);

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Search error for payload {Payload}: {(int)response.StatusCode} {ResponsePhrase}. Body: {ErrorText}", jsonPayload, (int)response.StatusCode, response.ReasonPhrase, errorText);

            return Array.Empty<SearchResult>();
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var searchResponse = JsonSerializer.Deserialize<FirecrawlSearchResponse>(content);

        if (searchResponse?.data == null)
        {
            _logger.LogWarning("Search returned no data for query '{Query}'", query);
            return Array.Empty<SearchResult>();
        }

        var results = searchResponse.data.Select(d => new SearchResult(d.url, d.title, d.snippet)).ToList();
        _logger.LogInformation("Search completed for query '{Query}': found {Count} results", query, results.Count);
        return results;
    }

    public async Task<string> FetchContentAsync(string url, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v1/scrape");
        // request.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var payload = new
        {
            url = url,
            timeout = 60000,
            formats = new[] { "markdown" }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        _logger.LogDebug("Fetching content for URL: {Url}", url);

       using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Firecrawl error for URL {Url}: {(int)response.StatusCode} {ResponsePhrase}. Body: {ErrorText}", url, (int)response.StatusCode, response.ReasonPhrase, errorText);

            return string.Empty;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var scrapeResponse = JsonSerializer.Deserialize<FirecrawlScrapeResponse>(content);

        _logger.LogInformation("Fetched content from URL {Url} with length {Length}", url, content.Length);
        return scrapeResponse?.markdown ?? string.Empty;
    }

    private class FirecrawlSearchResponse
    {
        public FirecrawlSearchData[]? data { get; set; }
    }

    private class FirecrawlSearchData
    {
        public string? url { get; set; }
        public string? title { get; set; }
        public string? snippet { get; set; }
    }

    private class FirecrawlScrapeResponse
    {
        public string? markdown { get; set; }
    }
}
