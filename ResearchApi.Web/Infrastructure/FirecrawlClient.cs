using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options, ILogger<FirecrawlClient> logger)
    : ISearchClient, ICrawlClient
{
   public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? location = null,
        CancellationToken ct = default)
    {
        var url = $"{options.Value.BaseUrl}/v1/search";

        object payload;

        logger.LogInformation("[SearchAsync]: query:{query}, limit:{limit}, location: {location}", query, limit, location);

        if (!string.IsNullOrWhiteSpace(location))
        {
            payload = new
            {
                query,
                limit,
                location,
                ignoreInvalidURLs = true
            };
        }
        else
        {
            payload = new
            {
                query,
                limit
            };
        }

        var jsonPayload = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        logger.LogDebug(
            "Executing search for query '{Query}' with limit {Limit}, location '{Location}'",
            query,
            limit,
            location);

        var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Search error for payload {Payload}: {(int)response.StatusCode} {ResponsePhrase}. Body: {ErrorText}",
                jsonPayload,
                (int)response.StatusCode,
                response.ReasonPhrase,
                errorText);

            return Array.Empty<SearchResult>();
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var searchResponse = JsonSerializer.Deserialize<FirecrawlSearchResponse>(content);

        if (searchResponse?.data == null)
        {
            logger.LogWarning("Search returned no data for query '{Query}'", query);
            return Array.Empty<SearchResult>();
        }

        var results = searchResponse.data
            .Select(d => new SearchResult(d.url, d.title, d.description))
            .ToList();

        logger.LogInformation("Search completed for query '{Query}': found {Count} results", query, results.Count);

        return results;
    }

    public async Task<string> FetchContentAsync(string url, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.BaseUrl}/v1/scrape");

        var payload = new
        {
            url,
            timeout = httpClient.Timeout.TotalMilliseconds,
            formats = new[] { "markdown" }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        logger.LogDebug("Fetching content for URL: {Url}", url);

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            logger.LogError(
                "Firecrawl error for URL {Url}: {StatusCode} {ReasonPhrase}. Body: {ErrorText}",
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
            scrapeResponse = JsonSerializer.Deserialize<FirecrawlScrapeResponse>(content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize Firecrawl scrape response for URL {Url}. Raw: {Raw}", url, content);
            return string.Empty;
        }

        var markdown = scrapeResponse?.data?.markdown ?? string.Empty;

        logger.LogInformation(
            "Fetched markdown content from URL {Url} with length {Length}",
            url,
            markdown.Length);

        return markdown;
    }

    class FirecrawlSearchResponse
    {
        public FirecrawlSearchData[]? data { get; set; }
    }

    class FirecrawlSearchData
    {
        public required string url { get; set; }
        public required string title { get; set; }
        public required string description { get; set; }
    }

    class FirecrawlScrapeResponse
    {
        public bool success { get; set; }
        public FirecrawlScrapeData? data { get; set; }
    }

    class FirecrawlScrapeData
    {
        public string? markdown { get; set; }
    }
}
