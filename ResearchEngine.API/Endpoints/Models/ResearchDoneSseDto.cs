namespace ResearchEngine.API;

public static partial class ResearchApi
{
    public sealed record ResearchDoneSseDto(Guid JobId, string Status, Guid? SynthesisId);
}
