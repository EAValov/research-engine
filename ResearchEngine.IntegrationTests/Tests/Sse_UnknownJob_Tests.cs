using System.Net;
using System.Net.Http.Json;
using ResearchEngine.IntegrationTests.Infrastructure;
using ResearchEngine.API;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sse_UnknownJob_Tests : IntegrationTestBase
{
    public Sse_UnknownJob_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task EventsStream_WithUnknownJob_Returns404()
    {
        using var client = CreateClient();

        var unknown = Guid.NewGuid();

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{unknown}/events/stream-token");

        using var tokenResp = await client.SendAsync(tokenReq);
        Assert.Equal(HttpStatusCode.NotFound, tokenResp.StatusCode);
    }

    [Fact]
    public async Task EventsList_WithUnknownJob_Returns404()
    {
        using var client = CreateClient();

        var unknown = Guid.NewGuid();
        var resp = await client.GetAsync($"/api/jobs/{unknown}/events");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}