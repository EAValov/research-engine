using ResearchApi.Domain;

public sealed record WebhookPayload(
    Guid JobId,
    ResearchEventStage Stage,
    DateTimeOffset TimestampUtc,
    object Data
);
