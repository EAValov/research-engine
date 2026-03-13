using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using ResearchEngine.API;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class JobCancel_Tests : IntegrationTestBase
{
    public JobCancel_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task CancelJob_DuringSearching_ProducesCancelRequestedEvent_AndTerminates()
    {
        // Arrange: slow search to give us time to cancel
        await using var slowFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Force inline background runner so the test host executes the job immediately in-process
                services.RemoveAll<IBackgroundJobClient>();
                services.AddSingleton<IBackgroundJobClient, InlineBackgroundJobClient>();

                services.RemoveAll<ISearchClient>();
                services.AddSingleton<ISearchClient>(new SlowSearchClient(delay: TimeSpan.FromSeconds(10)));
            });
        });

        using var client = slowFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // 1) create job
        var jobId = await CreateJobAsync(client, "Test query: cancel job.");
        Assert.NotEqual(Guid.Empty, jobId);

        // 2) wait until we see at least one "Searching" or "Planning" event so we know job started
        var started = await WaitForAnyStageAsync(client, jobId, ["Planning", "Searching"], TimeSpan.FromSeconds(15));
        Assert.True(started, "Job did not start within timeout.");

        // 3) cancel job
        var cancelResp = await client.PostAsJsonAsync($"/api/research/jobs/{jobId}/cancel", new { reason = "test cancel" });
        Assert.Equal(HttpStatusCode.Accepted, cancelResp.StatusCode);

        // 4) ensure cancel requested event exists
        var eventsResp = await client.GetAsync($"/api/research/jobs/{jobId}/events");
        eventsResp.EnsureSuccessStatusCode();
        var eventsJson = await eventsResp.Content.ReadFromJsonAsync<JsonElement>();
        var events = eventsJson.EnumerateArray().ToList();

        Assert.Contains(events, e => e.GetProperty("message").GetString()!.Contains("Cancel requested", StringComparison.OrdinalIgnoreCase));

        var events_str = eventsResp.Content.ReadAsStringAsync();
       
        // 5) SSE must eventually terminate (Canceled/Failed).
        var (status, _, _) = await SseTestHelpers.WaitForDoneAsync(client, jobId, TimeSpan.FromSeconds(60));
        Assert.True(status is "Canceled");
    }

    private sealed class SlowSearchClient : ISearchClient
    {
        private readonly TimeSpan _delay;
        public SlowSearchClient(TimeSpan delay) => _delay = delay;

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, string? location = null, CancellationToken ct = default)
        {
            await Task.Delay(_delay, ct);

            // Return at least one URL so the pipeline progresses into URL processing (or not, depending on timing)
            return [new SearchResult ("https://example.com",  "Example",  "Example snippet" ) ];
        }
    }

    private static async Task<bool> WaitForAnyStageAsync(HttpClient client, Guid jobId, IReadOnlyCollection<string> anyOfStages, TimeSpan timeout)
    {
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"/api/research/jobs/{jobId}/events/stream-token");

        using var tokenResp = await client.SendAsync(tokenReq);
        tokenResp.EnsureSuccessStatusCode();

        var token = await tokenResp.Content.ReadFromJsonAsync<CreateSseTokenResponse>()
                    ?? throw new InvalidOperationException("Token response was empty.");
                    
        // Open stream with ticket
        using var req = new HttpRequestMessage(HttpMethod.Get, token.StreamUrl);
        req.Headers.Add("Accept", "text/event-stream");

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var cts = new CancellationTokenSource(timeout);

        await foreach (var frame in SseReader.ReadAsync(stream, cts.Token))
        {
            if (frame.Event != "event")
                continue;

            using var doc = SseReader.ParseJson(frame);
            var stage = GetStageName(doc.RootElement.GetProperty("stage"));
            if (stage is not null && anyOfStages.Contains(stage))
                return true;
        }

        return false;
    }
}
