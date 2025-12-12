using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResearchApi.Configuration;
using ResearchApi.Configuration.Crawl4AI;
using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public sealed class Crawl4AiClient : ICrawlClient
{
    private readonly HttpClient _httpClient;
    private readonly Crawl4AiOptions _options;
    private readonly ILogger<Crawl4AiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public Crawl4AiClient(
        HttpClient httpClient,
        IOptions<Crawl4AiOptions> options,
        ILogger<Crawl4AiClient> logger)
    {
        _httpClient = httpClient;
        _options    = options.Value;
        _logger     = logger;

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        }
    }

    public async Task<string> FetchContentAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        const string endpoint = "/crawl";

        // Anti-bot & markdown-focused defaults
        var browserConfig = new BrowserConfig
        {
            BrowserType      = "chromium",
            Headless         = true,
            BrowserMode      = "dedicated",
            Verbose          = false,
            IgnoreHttpsErrors = true,
            JavaScriptEnabled = true,
            TextMode         = false,
            LightMode        = false,
            EnableStealth    = true
        };

        var crawlerConfig = new CrawlerRunConfig
        {
            Stream = false, // sync
            CacheMode = new TypedConfig<string>
            {
                Type   = "CacheMode",
                Params = "bypass"
            },
            WordCountThreshold   = 100,
            WaitUntil            = "networkidle",
            ScanFullPage         = true,
            SimulateUser         = true,
            OverrideNavigator    = true,
            Magic                = true,
            RemoveOverlayElements = true,
            WaitForImages        = false,
            Screenshot           = false,
            Pdf                  = false
        };

        var payload = new
        {
            urls = new[] { url },
            browser_config = new TypedConfig<BrowserConfig>
            {
                Type   = "BrowserConfig",
                Params = browserConfig
            },
            crawler_config = new TypedConfig<CrawlerRunConfig>
            {
                Type   = "CrawlerRunConfig",
                Params = crawlerConfig
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiToken);
        }

        _logger.LogDebug("Crawl4AI crawl: payload={payload}", jsonPayload);

        try
        {
            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Crawl4AI crawl error for URL {Url}: {StatusCode} {Reason}. Body: {ErrorText}",
                    url,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    errorText);

                return string.Empty;
            }

            var content = await response.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (!root.TryGetProperty("results", out var resultsElem) ||
                    resultsElem.ValueKind != JsonValueKind.Array ||
                    resultsElem.GetArrayLength() == 0)
                {
                    _logger.LogWarning(
                        "Crawl4AI crawl returned no results for URL {Url}. Raw: {Raw}",
                        url,
                        content);
                    return string.Empty;
                }

                var first = resultsElem[0];
                var text  = ExtractBestText(url, first, content);

                LogSuccess(url, text);
                return text;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "Failed to deserialize Crawl4AI crawl response for URL {Url}. Raw: {Raw}",
                    url,
                    content);
                return string.Empty;
            }
        }
        catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(oce,
                "Crawl4AI crawl request timed out for URL {Url}",
                url);
            return string.Empty;
        }
        catch (HttpRequestException hre)
        {
            _logger.LogWarning(hre,
                "Crawl4AI crawl request failed for URL {Url}",
                url);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during Crawl4AI crawl for URL {Url}",
                url);
            return string.Empty;
        }
    }


    /// <summary>
    /// Extracts best-effort textual content from a single CrawlResult:
    /// prefers markdown (fit/raw), then cleaned_html, fit_html, html, and finally raw JSON.
    /// </summary>
    private string ExtractBestText(string url, JsonElement resultElem, string rawJsonForDebug)
    {
        // 1) markdown.{fit_markdown, raw_markdown}
        if (resultElem.TryGetProperty("markdown", out var mdElem) &&
            mdElem.ValueKind == JsonValueKind.Object)
        {
            string? fitMarkdown = null;
            string? rawMarkdown = null;

            if (mdElem.TryGetProperty("fit_markdown", out var fitMdElem) &&
                fitMdElem.ValueKind == JsonValueKind.String)
            {
                fitMarkdown = fitMdElem.GetString();
            }

            if (mdElem.TryGetProperty("raw_markdown", out var rawMdElem) &&
                rawMdElem.ValueKind == JsonValueKind.String)
            {
                rawMarkdown = rawMdElem.GetString();
            }

            var chosen = !string.IsNullOrWhiteSpace(fitMarkdown)
                ? fitMarkdown
                : rawMarkdown;

            if (!string.IsNullOrEmpty(chosen))
                return chosen;
        }

        // 2) cleaned_html
        if (resultElem.TryGetProperty("cleaned_html", out var cleanedElem) &&
            cleanedElem.ValueKind == JsonValueKind.String)
        {
            var cleaned = cleanedElem.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(cleaned))
                return cleaned;
        }

        // 3) fit_html
        if (resultElem.TryGetProperty("fit_html", out var fitHtmlElem) &&
            fitHtmlElem.ValueKind == JsonValueKind.String)
        {
            var fitHtml = fitHtmlElem.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(fitHtml))
                return fitHtml;
        }

        // 4) raw html
        if (resultElem.TryGetProperty("html", out var htmlElem) &&
            htmlElem.ValueKind == JsonValueKind.String)
        {
            var html = htmlElem.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(html))
                return html;
        }

        // 5) last resort: raw JSON (so you at least see *something*)
        _logger.LogDebug(
            "Crawl4AI returned no markdown/html for URL {Url}. Raw result: {Raw}",
            url,
            rawJsonForDebug);

        return resultElem.GetRawText();
    }

    private void LogSuccess(string url, string text)
    {
        _logger.LogInformation(
            "Fetched content from URL {Url} using Crawl4AI. Length={Length}",
            url,
            text.Length);
    }

}
