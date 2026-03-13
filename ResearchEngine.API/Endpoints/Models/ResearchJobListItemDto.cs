namespace ResearchEngine.API;

public sealed record ResearchJobListItemDto(
    Guid Id,
    string Query,
    int Breadth,
    int Depth,
    string Status,
    string TargetLanguage,
    string? Region,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
