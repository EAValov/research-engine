using System.Net.Http.Json;
using System.Text.Json;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using ResearchEngine.Web;
using Xunit;
using static ResearchEngine.IntegrationTests.Helpers.SseTestHelpers;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sse_Resume_NoDuplicates_Tests : IntegrationTestBase
{
    public Sse_Resume_NoDuplicates_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task EventsStream_ReconnectWithLastEventId_DoesNotDuplicateEvents_AndStillTerminates()
    {
        using var client = CreateClient();

        var jobId = await CreateJobAsync(client, "Test query: SSE resume behavior.");

        // Open SSE and read a few frames, record max event-id we saw, then "disconnect" early.
        var (maxEventIdSeen, sawAnyEvent) = await ReadSomeEventsAndDisconnectAsync(
            client,
            jobId,
            stopAfterEventFrames: 3,
            timeout: TimeSpan.FromSeconds(20));

        Assert.True(sawAnyEvent);
        Assert.True(maxEventIdSeen > 0);

        // Reconnect with Last-Event-ID and ensure:
        // 1) we do not receive any "event" frames with id <= maxEventIdSeen
        // 2) we eventually get "done"

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"/api/research/jobs/{jobId}/events/stream-token");

        using var tokenResp = await client.SendAsync(tokenReq);
        tokenResp.EnsureSuccessStatusCode();

        var token = await tokenResp.Content.ReadFromJsonAsync<CreateSseTokenResponse>()
                    ?? throw new InvalidOperationException("Token response was empty.");
                    
        // Open stream with ticket
        using var req = new HttpRequestMessage(HttpMethod.Get, token.StreamUrl);
        req.Headers.Add("Accept", "text/event-stream");
        req.Headers.Add("Last-Event-ID", maxEventIdSeen.ToString());

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        string? terminal = null;
        await foreach (var frame in SseReader.ReadAsync(stream, cts.Token))
        {
            Assert.False(string.IsNullOrWhiteSpace(frame.Event));

            if (frame.Event == "event")
            {
                // Ensure no duplicates
                Assert.True(int.TryParse(frame.Id, out var id), "SSE event frames must have numeric id.");
                Assert.True(id > maxEventIdSeen, $"Received duplicate event id={id} (<= {maxEventIdSeen}).");
            }

            if (frame.Event == "done")
            {
                using var doc = SseReader.ParseJson(frame);
                var status = doc.RootElement.GetProperty("status").GetString();
                Assert.True(status is "Completed" or "Failed");
                terminal = status;
                break;
            }
        }

        Assert.Equal("Completed", terminal);
    }

    private static async Task<(int MaxEventIdSeen, bool SawAnyEvent)> ReadSomeEventsAndDisconnectAsync(
        HttpClient client,
        Guid jobId,
        int stopAfterEventFrames,
        TimeSpan timeout)
    {
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
        using var cts = new CancellationTokenSource(timeout);

        var maxId = 0;
        var eventFrames = 0;
        var sawAny = false;

        await foreach (var frame in SseReader.ReadAsync(stream, cts.Token))
        {
            if (frame.Event == "event")
            {
                sawAny = true;
                eventFrames++;

                if (int.TryParse(frame.Id, out var id))
                    maxId = Math.Max(maxId, id);

                if (eventFrames >= stopAfterEventFrames)
                    break; // "disconnect" by disposing stream/response
            }
        }

        return (maxId, sawAny);
    }
}