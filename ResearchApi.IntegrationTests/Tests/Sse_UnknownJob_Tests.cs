using System.Net;
using ResearchApi.IntegrationTests.Infrastructure;

namespace ResearchApi.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sse_UnknownJob_Tests : IntegrationTestBase
{
    public Sse_UnknownJob_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task EventsStream_WithUnknownJob_Returns404()
    {
        using var client = CreateClient();

        var unknown = Guid.NewGuid();

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/research/jobs/{unknown}/events/stream");
        req.Headers.Add("Accept", "text/event-stream");

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task EventsList_WithUnknownJob_Returns404()
    {
        using var client = CreateClient();

        var unknown = Guid.NewGuid();
        var resp = await client.GetAsync($"/api/research/jobs/{unknown}/events");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}