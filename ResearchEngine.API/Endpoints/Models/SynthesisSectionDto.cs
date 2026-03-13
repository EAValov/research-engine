namespace ResearchEngine.API;

public sealed record SynthesisSectionDto(
    Guid Id,
    Guid SynthesisId,
    Guid SectionKey,
    int Index,
    string Title,
    string? Description,
    bool IsConclusion,
    string? Summary,
    string? ContentMarkdown,
    DateTimeOffset CreatedAt);
