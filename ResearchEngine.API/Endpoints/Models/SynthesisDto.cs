namespace ResearchEngine.API;

public sealed record SynthesisDto(
    Guid Id,
    Guid JobId,
    Guid? ParentSynthesisId,
    string Status,
    string ChatModelName,
    string EmbeddingModelName,
    string? Outline,
    string? Instructions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    IReadOnlyList<SynthesisSectionDto> Sections);
