using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class ApiPing_Tests : IntegrationTestBase
{
    public ApiPing_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        using var client = CreateClient();

        var resp = await client.GetAsync("/api/ping");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(resp.Headers.TryGetValues("X-Correlation-ID", out var correlationIds));
        Assert.False(string.IsNullOrWhiteSpace(correlationIds.Single()));

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", payload.GetProperty("status").GetString());
    }
}
