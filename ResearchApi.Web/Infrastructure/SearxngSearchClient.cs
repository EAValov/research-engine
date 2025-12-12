using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector.EntityFrameworkCore;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public sealed class SearxngSearchClient : ISearchClient
{
    private readonly HttpClient _httpClient;
    private readonly SearxngOptions _options;
    private readonly ILogger<SearxngSearchClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private sealed class SearxngResponse
    {
        public SearchResult[]? results { get; set; }
    }

    public SearxngSearchClient(
        HttpClient httpClient,
        IOptions<SearxngOptions> options,
        ILogger<SearxngSearchClient> logger)
    {
        _httpClient  = httpClient;
        _options     = options.Value;
        _logger      = logger;

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? location = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        limit = Math.Max(1, limit);

        var language = _options.DefaultLanguage ?? "en";

        // Searxng doesn't support "location" the same way as Firecrawl;
        // you could map it to &safesearch or &categories if you want.
        var url = $"/search?q={Uri.EscapeDataString(query)}" +
                  $"&format=json" +
        //        $"&language={Uri.EscapeDataString(language)}" +
                  $"&num={limit}";

        _logger.LogDebug("Searxng search: {Url}", url);

        try
        {
            using var response = await _httpClient.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Searxng search error for query '{Query}': {StatusCode} {Reason}. Body: {Body}",
                    query,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    errorText);

                return Array.Empty<SearchResult>();
            }

            var content = await response.Content.ReadAsStringAsync(ct);

            SearxngResponse? result;
            try
            {
                result = JsonSerializer.Deserialize<SearxngResponse>(content, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Failed to deserialize Searxng search response for query '{Query}'. Raw: {Raw}",
                    query,
                    content);
                return Array.Empty<SearchResult>();
            }

            var items = result?.results;
            if (items is null || items.Length == 0)
                return Array.Empty<SearchResult>();

            var mapped = items
                .Where(r => !string.IsNullOrWhiteSpace(r.Url))
                .Select(r =>
                    new SearchResult(
                        r.Url!,
                        r.Title ?? r.Url!,
                        r.Content ?? string.Empty))
                .Take(limit)
                .ToList();

            _logger.LogInformation(
                "Searxng search completed for query '{Query}': {Count} results",
                query,
                mapped.Count);

            return mapped;
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(oce,
                "Searxng search request timed out for query '{Query}'",
                query);
            return Array.Empty<SearchResult>();
        }
        catch (HttpRequestException hre)
        {
            _logger.LogWarning(hre,
                "Searxng search request failed for query '{Query}'",
                query);
            return Array.Empty<SearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during Searxng search for query '{Query}'",
                query);
            return Array.Empty<SearchResult>();
        }
    }
}
