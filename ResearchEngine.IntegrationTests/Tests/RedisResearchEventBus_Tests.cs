using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchEngine.Domain;
using ResearchEngine.Infrastructure;
using ResearchEngine.IntegrationTests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace ResearchEngine.IntegrationTests.Tests;

[Collection(ContainersCollection.Name)]
public sealed class RedisResearchEventBus_Tests
{
    private readonly ContainersFixture _containers;

    public RedisResearchEventBus_Tests(ContainersFixture containers)
        => _containers = containers;

    [Fact]
    public async Task SubscribeAsync_DoesNotDropEvents_WhenConsumerTemporarilyFallsBehind()
    {
        var redisHost = _containers.Redis.Hostname;
        var redisPort = _containers.Redis.GetMappedPublicPort(6379);

        using var mux = await ConnectionMultiplexer.ConnectAsync($"{redisHost}:{redisPort},abortConnect=false");

        var bus = new RedisResearchEventBus(mux, NullLogger<RedisResearchEventBus>.Instance);
        var jobId = Guid.NewGuid();
        var seenIds = new ConcurrentQueue<int>();
        var firstEventStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await bus.SubscribeAsync(
            jobId,
            async (ev, ct) =>
            {
                firstEventStarted.TrySetResult();
                await releaseConsumer.Task.WaitAsync(ct);
                seenIds.Enqueue(ev.Id);
            },
            CancellationToken.None);

        const int eventCount = 400;

        for (var i = 1; i <= eventCount; i++)
        {
            await bus.PublishAsync(
                jobId,
                new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Searching, $"event-{i}") { Id = i },
                CancellationToken.None);
        }

        using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await firstEventStarted.Task.WaitAsync(startCts.Token);

        releaseConsumer.TrySetResult();

        using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (seenIds.Count < eventCount && !drainCts.IsCancellationRequested)
            await Task.Delay(25, drainCts.Token);

        Assert.Equal(Enumerable.Range(1, eventCount).ToArray(), seenIds.ToArray());
    }
}
