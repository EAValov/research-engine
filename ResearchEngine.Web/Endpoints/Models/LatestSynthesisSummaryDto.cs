namespace ResearchEngine.Web;

public sealed record LatestSynthesisSummaryDto(
    Guid Id,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
