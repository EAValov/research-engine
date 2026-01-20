namespace ResearchEngine.Web;

public sealed record LatestSynthesisResponse(Guid JobId, SynthesisDto? Synthesis);
