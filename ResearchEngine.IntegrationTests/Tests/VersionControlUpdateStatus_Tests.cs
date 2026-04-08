using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class VersionControlUpdateStatus_Tests : IntegrationTestBase
{
    public VersionControlUpdateStatus_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task UpdateStatus_ReturnsConfiguredPayload()
    {
        using var scope = Factory.Services.CreateScope();
        var fakeService = scope.ServiceProvider.GetRequiredService<FakeReleaseUpdateService>();
        fakeService.Response = new(
            CheckEnabled: true,
            CurrentVersion: "1.0.0",
            LatestVersion: "1.1.0",
            UpdateAvailable: true,
            ReleaseUrl: "https://github.com/EAValov/research-engine/releases/tag/v1.1.0",
            ReleaseTitle: "Research Engine v1.1.0",
            PublishedAtUtc: DateTimeOffset.Parse("2026-04-03T12:00:00Z"));

        using var client = CreateClient();

        var response = await client.GetAsync("/version-control/update-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.GetProperty("checkEnabled").GetBoolean());
        Assert.Equal("1.0.0", payload.GetProperty("currentVersion").GetString());
        Assert.Equal("1.1.0", payload.GetProperty("latestVersion").GetString());
        Assert.True(payload.GetProperty("updateAvailable").GetBoolean());
        Assert.Equal(
            "https://github.com/EAValov/research-engine/releases/tag/v1.1.0",
            payload.GetProperty("releaseUrl").GetString());
    }
}
