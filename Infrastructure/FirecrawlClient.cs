using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options)
    : ISearchClient, ICrawlClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly FirecrawlOptions _options = options.Value;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? languageCode = null,
        string? regionCode = null,
        CancellationToken ct = default )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v1/search");
   
        var payload = new { query, limit };

        var jsonPayload = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var searchResponse = JsonSerializer.Deserialize<FirecrawlSearchResponse>(content);

        if (searchResponse?.data == null)
        {
            return Array.Empty<SearchResult>();
        }

        var results = searchResponse.data.Select(d => new SearchResult(d.url, d.title, d.snippet)).ToList();
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

       using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"Firecrawl error for URL {url}: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {errorText}");

            return string.Empty;
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var scrapeResponse = JsonSerializer.Deserialize<FirecrawlScrapeResponse>(content);

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
