namespace ResearchEngine.API;

public sealed record CancelJobResponse(Guid JobId, string Status);
