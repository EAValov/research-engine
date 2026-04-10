using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResearchEngine.API;
using ResearchEngine.Domain;
using ResearchEngine.IntegrationTests.Helpers;
using ResearchEngine.IntegrationTests.Infrastructure;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class Sse_Replay_Live_Handoff_Tests : IntegrationTestBase
{
    public Sse_Replay_Live_Handoff_Tests(ContainersFixture containers) : base(containers) { }

    [Fact]
    public async Task EventsStream_ReplaysPersistedEvents_BeforeBufferedLiveEvents()
    {
        var replayGate = new ReplayDelayGate();

        using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(replayGate);
                services.RemoveAll<IResearchEventRepository>();
                services.AddScoped<IResearchEventRepository>(sp =>
                    new DelayedReplayEventRepository(
                        sp.GetRequiredService<IResearchJobStore>(),
                        sp.GetRequiredService<ReplayDelayGate>()));
            });
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Guid jobId;
        List<int> initialIds;

        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IResearchJobStore>();

            var job = await store.CreateJobAsync(
                query: "SSE replay/live handoff regression test.",
                clarifications: [],
                breadth: 1,
                depth: 1,
                discoveryMode: SourceDiscoveryMode.Balanced,
                language: "en",
                region: null);

            jobId = job.Id;

            await store.AppendEventAsync(jobId, new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Planning, "stored-1"));
            await store.AppendEventAsync(jobId, new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Searching, "stored-2"));

            initialIds = (await store.GetEventsAsync(jobId)).Select(x => x.Id).OrderBy(x => x).ToList();
        }

        replayGate.DelayNextReplay();

        const int liveEventCount = 3;
        var streamTask = ReadEventIdsAsync(client, jobId, expectedCount: initialIds.Count + liveEventCount, TimeSpan.FromSeconds(20));

        using var enterCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await replayGate.WaitUntilReplayStartedAsync(enterCts.Token);

        List<int> appendedIds;
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IResearchJobStore>();

            appendedIds = [];
            appendedIds.Add(await store.AppendEventAsync(jobId, new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Summarizing, "live-1")));
            appendedIds.Add(await store.AppendEventAsync(jobId, new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Metrics, "live-2")));
            appendedIds.Add(await store.AppendEventAsync(jobId, new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Searching, "live-3")));
        }

        replayGate.ReleaseReplay();

        var streamedIds = await streamTask;

        Assert.Equal(initialIds.Concat(appendedIds).ToList(), streamedIds);
    }

    private static async Task<List<int>> ReadEventIdsAsync(HttpClient client, Guid jobId, int expectedCount, TimeSpan timeout)
    {
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{jobId}/events/stream-token");
        using var tokenResp = await client.SendAsync(tokenReq);
        tokenResp.EnsureSuccessStatusCode();

        var token = await tokenResp.Content.ReadFromJsonAsync<CreateSseTokenResponse>()
                    ?? throw new InvalidOperationException("Token response was empty.");

        using var req = new HttpRequestMessage(HttpMethod.Get, token.StreamUrl);
        req.Headers.Add("Accept", "text/event-stream");
        req.Headers.Add("Last-Event-ID", "0");

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync();
        using var cts = new CancellationTokenSource(timeout);

        var eventIds = new List<int>(expectedCount);

        try
        {
            await foreach (var frame in SseReader.ReadAsync(stream, cts.Token))
            {
                if (frame.Event != "event")
                    continue;

                Assert.True(int.TryParse(frame.Id, out var id), "Expected numeric SSE event id.");
                eventIds.Add(id);

                if (eventIds.Count == expectedCount)
                    return eventIds;
            }
        }
        catch (OperationCanceledException)
        {
        }

        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for {expectedCount} SSE event frames. Received: [{string.Join(", ", eventIds)}]");
    }

    private sealed class DelayedReplayEventRepository : IResearchEventRepository
    {
        private readonly IResearchJobStore _inner;
        private readonly ReplayDelayGate _gate;

        public DelayedReplayEventRepository(IResearchJobStore inner, ReplayDelayGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default)
            => _inner.AppendEventAsync(jobId, ev, ct);

        public async Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
        {
            await _gate.DelayIfArmedAsync(ct);
            return await _inner.GetEventsAsync(jobId, ct);
        }
    }

    private sealed class ReplayDelayGate
    {
        private readonly object _lock = new();
        private TaskCompletionSource? _entered;
        private TaskCompletionSource? _release;
        private bool _delayNextReplay;

        public void DelayNextReplay()
        {
            lock (_lock)
            {
                _delayNextReplay = true;
                _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public async Task DelayIfArmedAsync(CancellationToken ct)
        {
            TaskCompletionSource? entered;
            TaskCompletionSource? release;

            lock (_lock)
            {
                if (!_delayNextReplay)
                    return;

                _delayNextReplay = false;
                entered = _entered;
                release = _release;
            }

            entered?.TrySetResult();

            if (release is not null)
                await release.Task.WaitAsync(ct);
        }

        public Task WaitUntilReplayStartedAsync(CancellationToken ct)
        {
            Task task;

            lock (_lock)
            {
                task = _entered?.Task ?? Task.CompletedTask;
            }

            return task.WaitAsync(ct);
        }

        public void ReleaseReplay()
        {
            lock (_lock)
            {
                _release?.TrySetResult();
            }
        }
    }
}
