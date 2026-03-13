namespace ResearchEngine.API;

public sealed record LatestSynthesisSummaryDto(
    Guid Id,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
