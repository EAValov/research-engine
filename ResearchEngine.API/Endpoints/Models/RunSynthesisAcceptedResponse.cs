namespace ResearchEngine.API;

public sealed record RunSynthesisAcceptedResponse(
    Guid JobId,
    Guid SynthesisId,
    string HangfireJobId,
    string Status,
    DateTimeOffset CreatedAt);
