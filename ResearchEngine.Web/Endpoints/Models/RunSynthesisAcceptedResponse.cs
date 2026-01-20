namespace ResearchEngine.Web;

public sealed record RunSynthesisAcceptedResponse(
    Guid JobId,
    Guid SynthesisId,
    string HangfireJobId,
    string Status,
    DateTimeOffset CreatedAt);
