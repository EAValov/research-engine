using ResearchEngine.API;

namespace ResearchEngine.IntegrationTests.Infrastructure;

public sealed class FakeReleaseUpdateService : IReleaseUpdateService
{
    public UpdateStatusResponse Response { get; set; } = new(
        CheckEnabled: false,
        CurrentVersion: "dev",
        LatestVersion: null,
        UpdateAvailable: false,
        ReleaseUrl: null,
        ReleaseTitle: null,
        PublishedAtUtc: null);

    public Task<UpdateStatusResponse> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(Response);
}
