using System.ComponentModel.DataAnnotations;
using ResearchApi.Domain;

public sealed record WebhookSubscription(
    Guid JobId,
    Uri Url,
    string? Secret,
    ResearchEventStage[] Stages,        
    DateTimeOffset CreatedUtc
);
