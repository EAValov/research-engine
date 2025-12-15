using System.Text.Json;
using ResearchApi.Domain;
using StackExchange.Redis;

namespace ResearchApi.Infrastructure;

public sealed class RedisWebhookSubscriptionStore : IWebhookSubscriptionStore
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisWebhookSubscriptionStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly TimeSpan? SubscriptionTtl = TimeSpan.FromDays(1);

    public RedisWebhookSubscriptionStore(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisWebhookSubscriptionStore> logger)
    {
        _db = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task SaveAsync(WebhookSubscription sub, CancellationToken ct)
    {
        try
        {
            var key = Key(sub.JobId);
            var json = JsonSerializer.Serialize(sub, JsonOptions);

            // StackExchange.Redis doesn't support CT.
            await _db.StringSetAsync(key, json, expiry: SubscriptionTtl).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save webhook subscription for job {JobId}", sub.JobId);
            throw;
        }
    }

    public async Task<WebhookSubscription?> GetAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            var key = Key(jobId);
            var json = await _db.StringGetAsync(key).ConfigureAwait(false);

            if (!json.HasValue)
                return null;

            return JsonSerializer.Deserialize<WebhookSubscription>(json.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get webhook subscription for job {JobId}", jobId);
            return null; // (soft fail)
        }
    }

    public async Task DeleteAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            var key = Key(jobId);
            await _db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete webhook subscription for job {JobId}", jobId);
        }
    }

    private static string Key(Guid jobId) => $"dr:webhook:sub:{jobId}";
}


