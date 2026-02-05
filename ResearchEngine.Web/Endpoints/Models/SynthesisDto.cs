namespace ResearchEngine.Web;

public sealed record SynthesisDto(
    Guid Id,
    Guid JobId,
    Guid? ParentSynthesisId,
    string Status,
    string? Outline,
    string? Instructions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    IReadOnlyList<SynthesisSectionDto> Sections);
