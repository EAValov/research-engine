namespace ResearchApi.Domain;

public interface IWebhookDispatcher
{
    Task EnqueueAsync(WebhookDeliveryRequest request, CancellationToken ct);
}
