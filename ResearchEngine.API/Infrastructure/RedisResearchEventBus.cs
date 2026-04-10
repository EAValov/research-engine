using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using ResearchEngine.Domain;

namespace ResearchEngine.Infrastructure;

public sealed class RedisResearchEventBus : IResearchEventBus
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisResearchEventBus> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisResearchEventBus(IConnectionMultiplexer redis, ILogger<RedisResearchEventBus> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task PublishAsync(Guid jobId, ResearchEvent ev, CancellationToken ct)
    {
        try
        {
            var channel = RedisChannel.Literal($"dr:job:{jobId}:events");
            var json = JsonSerializer.Serialize(ev, _jsonOptions);
            await _redis.GetSubscriber().PublishAsync(channel, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event for job {JobId}", jobId);
        }
    }

    public async Task<IAsyncDisposable> SubscribeAsync(
        Guid jobId,
        Func<ResearchEvent, CancellationToken, Task> onEvent,
        CancellationToken ct)
    {
        var channel = RedisChannel.Literal($"dr:job:{jobId}:events");
        var subscriber = _redis.GetSubscriber();

        // Link cancellation so the subscription/consumer stops when caller cancels
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var dispatchState = new DispatchState();

        Action<RedisChannel, RedisValue> handler = (_, msg) =>
        {
            if (linkedCts.IsCancellationRequested)
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(msg.ToString()))
                    return;

                var ev = JsonSerializer.Deserialize<ResearchEvent>(msg.ToString(), _jsonOptions);
                if (ev is null)
                    return;

                lock (dispatchState.Lock)
                {
                    dispatchState.Task = dispatchState.Task
                        .ContinueWith(
                            async _ =>
                            {
                                if (linkedCts.IsCancellationRequested)
                                    return;

                                try
                                {
                                    await onEvent(ev, linkedCts.Token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                                {
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error in onEvent callback for job {JobId}", jobId);
                                }
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.None,
                            TaskScheduler.Default)
                        .Unwrap();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process Redis Pub/Sub message for job {JobId}", jobId);
            }
        };

        try
        {
            // Await subscription so errors propagate (redis down, auth, etc.)
            await subscriber.SubscribeAsync(channel, handler).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to Redis channel {Channel} for job {JobId}", channel.ToString(), jobId);
            linkedCts.Cancel();
            Task pendingDispatch;
            lock (dispatchState.Lock)
                pendingDispatch = dispatchState.Task;

            try { await pendingDispatch.ConfigureAwait(false); } catch { /* ignore */ }
            linkedCts.Dispose();
            throw;
        }

        return new SubscriptionHandle(subscriber, channel, handler, linkedCts, dispatchState, _logger);
    }

    private sealed class SubscriptionHandle : IAsyncDisposable
    {
        private readonly ISubscriber _subscriber;
        private readonly RedisChannel _channel;
        private readonly Action<RedisChannel, RedisValue> _handler;
        private readonly CancellationTokenSource _cts;
        private readonly DispatchState _dispatchState;
        private readonly ILogger _logger;
        private int _disposed;

        public SubscriptionHandle(
            ISubscriber subscriber,
            RedisChannel channel,
            Action<RedisChannel, RedisValue> handler,
            CancellationTokenSource cts,
            DispatchState dispatchState,
            ILogger logger)
        {
            _subscriber = subscriber;
            _channel = channel;
            _handler = handler;
            _cts = cts;
            _dispatchState = dispatchState;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try { _cts.Cancel(); } catch { /* ignore */ }

            try
            {
                await _subscriber.UnsubscribeAsync(_channel, _handler).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from Redis channel {Channel}", _channel.ToString());
            }

            Task pendingDispatch;
            lock (_dispatchState.Lock)
                pendingDispatch = _dispatchState.Task;

            try { await pendingDispatch.ConfigureAwait(false); } catch { /* ignore */ }

            _cts.Dispose();
        }
    }

    private sealed class DispatchState
    {
        public object Lock { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
    }
}
