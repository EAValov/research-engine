namespace ResearchApi.Domain;

public interface IWebhookSubscriptionStore
{
    Task SaveAsync(WebhookSubscription sub, CancellationToken ct);
    Task<WebhookSubscription?> GetAsync(Guid jobId, CancellationToken ct);
    Task DeleteAsync(Guid jobId, CancellationToken ct);
}
