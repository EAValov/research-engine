namespace ResearchEngine.API;

public sealed record SynthesisListItemDto(
    Guid SynthesisId,
    Guid JobId,
    Guid? ParentSynthesisId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    int SectionCount);
