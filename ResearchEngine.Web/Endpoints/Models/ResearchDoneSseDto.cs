namespace ResearchEngine.Web;

public static partial class ResearchApi
{
    public sealed record ResearchDoneSseDto(Guid JobId, string Status, Guid? SynthesisId);
}
