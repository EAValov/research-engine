namespace ResearchEngine.API;

public sealed record SynthesisListItemDto(
    Guid SynthesisId,
    Guid JobId,
    Guid? ParentSynthesisId,
    string Status,
    string ChatModelName,
    string EmbeddingModelName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    int SectionCount);
