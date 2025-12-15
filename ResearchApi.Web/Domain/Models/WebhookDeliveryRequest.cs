using ResearchApi.Domain;

public sealed record WebhookDeliveryRequest(
    Guid JobId,
    ResearchEventStage Stage,
    DateTimeOffset TimestampUtc,
    object Data
);
