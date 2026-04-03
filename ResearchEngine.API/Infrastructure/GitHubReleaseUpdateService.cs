using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ResearchEngine.API;
using ResearchEngine.Configuration;

namespace ResearchEngine.Infrastructure;

public sealed class GitHubReleaseUpdateService(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IOptions<ReleaseCheckOptions> options)
    : IReleaseUpdateService
{
    public const string HttpClientName = "GitHubReleases";

    private readonly IConfiguration _configuration = configuration;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IMemoryCache _cache = cache;
    private readonly IOptions<ReleaseCheckOptions> _options = options;

    public async Task<UpdateStatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        var releaseCheck = _options.Value;
        var currentVersion = NormalizeVersion(_configuration["AppVersion"]);

        if (!releaseCheck.Enabled
            || !TryParseStableVersion(currentVersion, out var current)
            || string.IsNullOrWhiteSpace(releaseCheck.RepositoryOwner)
            || string.IsNullOrWhiteSpace(releaseCheck.RepositoryName))
        {
            return new UpdateStatusResponse(
                CheckEnabled: false,
                CurrentVersion: currentVersion,
                LatestVersion: null,
                UpdateAvailable: false,
                ReleaseUrl: null,
                ReleaseTitle: null,
                PublishedAtUtc: null);
        }

        var latestRelease = await GetLatestReleaseAsync(releaseCheck, ct);
        if (latestRelease is null
            || !TryParseStableVersion(latestRelease.Version, out var latest))
        {
            return new UpdateStatusResponse(
                CheckEnabled: true,
                CurrentVersion: currentVersion,
                LatestVersion: latestRelease?.Version,
                UpdateAvailable: false,
                ReleaseUrl: latestRelease?.HtmlUrl,
                ReleaseTitle: latestRelease?.Title,
                PublishedAtUtc: latestRelease?.PublishedAtUtc);
        }

        return new UpdateStatusResponse(
            CheckEnabled: true,
            CurrentVersion: currentVersion,
            LatestVersion: latestRelease.Version,
            UpdateAvailable: latest > current,
            ReleaseUrl: latestRelease.HtmlUrl,
            ReleaseTitle: latestRelease.Title,
            PublishedAtUtc: latestRelease.PublishedAtUtc);
    }

    private async Task<CachedLatestRelease?> GetLatestReleaseAsync(
        ReleaseCheckOptions options,
        CancellationToken ct)
    {
        var cacheKey = $"release-check::{options.RepositoryOwner}/{options.RepositoryName}";
        if (_cache.TryGetValue(cacheKey, out CachedLatestRelease? cached))
            return cached;

        var latestRelease = await FetchLatestReleaseAsync(options, ct);
        var cacheMinutes = latestRelease is null
            ? ClampMinutes(options.FailureCacheMinutes, fallback: 30)
            : ClampMinutes(options.SuccessCacheMinutes, fallback: 360);

        _cache.Set(cacheKey, latestRelease, TimeSpan.FromMinutes(cacheMinutes));
        return latestRelease;
    }

    private async Task<CachedLatestRelease?> FetchLatestReleaseAsync(
        ReleaseCheckOptions options,
        CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(
                $"repos/{options.RepositoryOwner}/{options.RepositoryName}/releases/latest",
                ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<GitHubLatestReleaseDto>(cancellationToken: ct);
            if (payload is null)
                return null;

            return new CachedLatestRelease(
                NormalizeVersion(payload.TagName),
                payload.HtmlUrl,
                string.IsNullOrWhiteSpace(payload.Name) ? payload.TagName : payload.Name,
                payload.PublishedAt);
        }
        catch
        {
            return null;
        }
    }

    private static int ClampMinutes(int value, int fallback)
        => value <= 0 ? fallback : value;

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];

        return normalized;
    }

    private static bool TryParseStableVersion(string? value, out Version version)
    {
        version = new Version(0, 0);

        var normalized = NormalizeVersion(value);
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        if (!Version.TryParse(normalized, out var parsed) || parsed is null)
            return false;

        version = parsed;
        return true;
    }

    private sealed record CachedLatestRelease(
        string Version,
        string? HtmlUrl,
        string? Title,
        DateTimeOffset? PublishedAtUtc);

    private sealed record GitHubLatestReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }
    }
}
