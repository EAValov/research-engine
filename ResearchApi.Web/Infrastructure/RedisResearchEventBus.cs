using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using ResearchApi.Domain;
using System.Threading.Channels;

namespace ResearchApi.Infrastructure;

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

        // Bounded buffer so a slow SSE client doesn't explode memory
        var buffer = Channel.CreateBounded<ResearchEvent>(new BoundedChannelOptions(capacity: 256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest // or DropWrite if you prefer
        });

        // Consume sequentially to preserve order and avoid per-message Task.Run
        var consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in buffer.Reader.ReadAllAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await onEvent(ev, linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in onEvent callback for job {JobId}", jobId);
                        // continue
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Consumer loop crashed for job {JobId}", jobId);
            }
        }, CancellationToken.None);

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

                buffer.Writer.TryWrite(ev);
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
            buffer.Writer.TryComplete(ex);
            linkedCts.Cancel();
            // ensure consumer stops
            try { await consumerTask.ConfigureAwait(false); } catch { /* ignore */ }
            linkedCts.Dispose();
            throw;
        }

        return new SubscriptionHandle(subscriber, channel, handler, linkedCts, buffer, consumerTask, _logger);
    }

    private sealed class SubscriptionHandle : IAsyncDisposable
    {
        private readonly ISubscriber _subscriber;
        private readonly RedisChannel _channel;
        private readonly Action<RedisChannel, RedisValue> _handler;
        private readonly CancellationTokenSource _cts;
        private readonly Channel<ResearchEvent> _buffer;
        private readonly Task _consumerTask;
        private readonly ILogger _logger;
        private int _disposed;

        public SubscriptionHandle(
            ISubscriber subscriber,
            RedisChannel channel,
            Action<RedisChannel, RedisValue> handler,
            CancellationTokenSource cts,
            Channel<ResearchEvent> buffer,
            Task consumerTask,
            ILogger logger)
        {
            _subscriber = subscriber;
            _channel = channel;
            _handler = handler;
            _cts = cts;
            _buffer = buffer;
            _consumerTask = consumerTask;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try { _cts.Cancel(); } catch { /* ignore */ }
            try { _buffer.Writer.TryComplete(); } catch { /* ignore */ }

            try
            {
                await _subscriber.UnsubscribeAsync(_channel, _handler).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing from Redis channel {Channel}", _channel.ToString());
            }

            try { await _consumerTask.ConfigureAwait(false); } catch { /* ignore */ }

            _cts.Dispose();
        }
    }
}