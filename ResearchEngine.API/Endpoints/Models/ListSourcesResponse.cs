namespace ResearchEngine.API;

public sealed record ListSourcesResponse(Guid JobId, int Count, IReadOnlyList<SourceListItemDto> Sources);
