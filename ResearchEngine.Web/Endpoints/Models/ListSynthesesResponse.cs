namespace ResearchEngine.Web;

public sealed record ListSynthesesResponse(
    Guid JobId,
    int Skip,
    int Take,
    int Count,
    IReadOnlyList<SynthesisListItemDto> Syntheses);
