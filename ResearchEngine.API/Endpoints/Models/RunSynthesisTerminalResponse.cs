namespace ResearchEngine.API;

public sealed record RunSynthesisTerminalResponse(
    Guid JobId,
    Guid SynthesisId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string Message);
