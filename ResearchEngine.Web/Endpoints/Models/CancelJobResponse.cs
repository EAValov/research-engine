namespace ResearchEngine.Web;

public sealed record CancelJobResponse(Guid JobId, string Status);
