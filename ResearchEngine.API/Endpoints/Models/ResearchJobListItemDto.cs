namespace ResearchEngine.API;

public sealed record ResearchJobListItemDto(
    Guid Id,
    string Query,
    string ChatModelName,
    string EmbeddingModelName,
    int Breadth,
    int Depth,
    string DiscoveryMode,
    string Status,
    string TargetLanguage,
    string? Region,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
