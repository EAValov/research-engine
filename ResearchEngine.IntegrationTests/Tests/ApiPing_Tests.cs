using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Infrastructure;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class ApiPing_Tests : IntegrationTestBase
{
    public ApiPing_Tests(ContainersFixture containers) : base(containers) { }

    [Theory]
    [InlineData("/api/ping")]
    [InlineData("/api/research/ping")]
    public async Task Ping_ReturnsOk(string route)
    {
        using var client = CreateClient();

        var resp = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", payload.GetProperty("status").GetString());
    }
}
