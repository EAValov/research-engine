namespace ResearchEngine.Web;

public sealed record SourceListItemDto(
    Guid SourceId,
    string Reference,
    string? Title,
    string? Language,
    string? Region,
    DateTimeOffset CreatedAt,
    int LearningCount);
