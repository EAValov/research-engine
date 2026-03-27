namespace ResearchEngine.API;

public sealed record SourceListItemDto(
    Guid SourceId,
    string Reference,
    string? Title,
    string? Domain,
    string? Language,
    string? Region,
    string Classification,
    string ReliabilityTier,
    double ReliabilityScore,
    bool IsPrimarySource,
    string ReliabilityRationale,
    DateTimeOffset CreatedAt,
    int LearningCount);
