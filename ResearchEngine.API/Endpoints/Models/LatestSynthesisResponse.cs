namespace ResearchEngine.API;

public sealed record LatestSynthesisResponse(Guid JobId, SynthesisDto? Synthesis);
