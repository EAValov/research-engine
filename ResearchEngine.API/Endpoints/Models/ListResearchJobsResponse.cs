namespace ResearchEngine.API;

public sealed record ListResearchJobsResponse(int Count, IReadOnlyList<ResearchJobListItemDto> Jobs);
