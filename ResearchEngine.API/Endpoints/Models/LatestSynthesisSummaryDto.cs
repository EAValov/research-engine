namespace ResearchEngine.API;

public sealed record LatestSynthesisSummaryDto(
    Guid Id,
    string Status,
    string ChatModelName,
    string EmbeddingModelName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
