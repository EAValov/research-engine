namespace ResearchEngine.API;

public sealed record ResearchJobListItemDto(
    Guid Id,
    string Query,
    string ChatModelName,
    string EmbeddingModelName,
    int Breadth,
    int Depth,
    string Status,
    string TargetLanguage,
    string? Region,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
