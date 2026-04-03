using System.Net.Http.Json;

namespace ResearchEngine.WebUI.Services;

public sealed class UpdateStatusService(HttpClient http)
{
    private readonly HttpClient _http = http;

    public async Task<UpdateStatusResult?> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<UpdateStatusResult>("/meta/update-status", ct);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record UpdateStatusResult(
    bool CheckEnabled,
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? ReleaseUrl,
    string? ReleaseTitle,
    DateTimeOffset? PublishedAtUtc
);
