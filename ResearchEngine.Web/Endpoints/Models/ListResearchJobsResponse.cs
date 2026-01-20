namespace ResearchEngine.Web;

public sealed record ListResearchJobsResponse(int Count, IReadOnlyList<ResearchJobListItemDto> Jobs);
