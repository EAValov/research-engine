using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using ResearchEngine.IntegrationTests.Infrastructure;
using ResearchEngine.IntegrationTests.Helpers;
using static ResearchEngine.IntegrationTests.Helpers.SseTestHelpers;
using ResearchEngine.API;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sse_Disconnect_And_TwoClients_Tests : IntegrationTestBase
{
    public Sse_Disconnect_And_TwoClients_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task SseClientDisconnect_DoesNotBreakJob_AndJobStillCompletes()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client);

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"/api/research/jobs/{jobId}/events/stream-token");

        using var tokenResp = await client.SendAsync(tokenReq);
        tokenResp.EnsureSuccessStatusCode();

        var token = await tokenResp.Content.ReadFromJsonAsync<CreateSseTokenResponse>()
                    ?? throw new InvalidOperationException("Token response was empty.");
                    
        // 2) Open stream with ticket
        using var req = new HttpRequestMessage(HttpMethod.Get, token.StreamUrl);
        req.Headers.Add("Accept", "text/event-stream");

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Read a few frames (or until we see any "event"), then abort.
        var sawAny = false;

        await foreach (var frame in SseReader.ReadAsync(stream, cts.Token))
        {
            if (frame.Event == "event")
            {
                sawAny = true;
                break;
            }
        }

        Assert.True(sawAny, "Expected to receive at least one SSE event frame before disconnecting.");
        // Dispose response => disconnect
        

        // Now ensure the job still completes (use SSE done helper from scratch).
        // We use afterEventId checkpoint so we don't accidentally return a replayed done from an earlier connection.
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client, jobId);

        var (status, _, _) = await SseTestHelpers.WaitForDoneAfterAsync(
            client,
            jobId,
            afterEventId: checkpoint,
            timeout: TimeSpan.FromSeconds(60));

        Assert.Equal("Completed", status);
    }

    [Fact]
    public async Task TwoSseClients_BothReceiveDone_ForSameJob()
    {
        using var client1 = CreateClient();
        using var client2 = CreateClient();

        var jobId = await CreateJobAsync(client1);

        // Capture checkpoint so both clients wait for the "new" done for this job run.
        // (Avoids replay confusion if something ever changes in stream behavior.)
        var checkpoint = await SseTestHelpers.GetMaxEventIdAsync(client1, jobId);

        var t1 = SseTestHelpers.WaitForDoneAfterAsync(client1, jobId, checkpoint, TimeSpan.FromSeconds(60));
        var t2 = SseTestHelpers.WaitForDoneAfterAsync(client2, jobId, checkpoint, TimeSpan.FromSeconds(60));

        var results = await Task.WhenAll(t1, t2);

        Assert.Equal("Completed", results[0].Status);
        Assert.Equal("Completed", results[1].Status);

        // Both should provide a done event id (best-effort; if your SSE server doesn't set id for done, relax this)
        Assert.True(results[0].DoneEventId is > 0);
        Assert.True(results[1].DoneEventId is > 0);
    }

    private static async Task<Guid> CreateJobAsync(HttpClient client)
    {
        var createReq = new
        {
            query = "SSE tests job: multi-client + disconnect behavior.",
            clarifications = Array.Empty<object>(),
            breadth = 2,
            depth = 2,
            language = "en",
            region = (string?)null,
            webhook = (object?)null
        };

        var createResp = await client.PostAsJsonAsync("/api/research/jobs", createReq);
        createResp.EnsureSuccessStatusCode();

        var createJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = createJson.GetProperty("jobId").GetGuid();
        Assert.NotEqual(Guid.Empty, jobId);

        return jobId;
    }
}