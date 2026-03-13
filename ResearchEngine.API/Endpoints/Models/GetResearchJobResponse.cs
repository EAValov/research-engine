namespace ResearchEngine.API;

public sealed record GetResearchJobResponse(
    Guid Id,
    string Query,
    int Breadth,
    int Depth,
    string Status,
    string TargetLanguage,
    string? Region,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ClarificationDto> Clarifications,
    int SourcesCount,
    int SynthesesCount,
    LatestSynthesisSummaryDto? LatestSynthesis);
