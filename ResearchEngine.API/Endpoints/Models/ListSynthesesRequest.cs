using Microsoft.AspNetCore.Mvc;

namespace ResearchEngine.API;
public sealed class ListSynthesesRequest
{
    [FromQuery(Name = "skip")]
    public int? Skip { get; init; }

    [FromQuery(Name = "take")]
    public int? Take { get; init; }

    public int SkipValue => Math.Max(Skip ?? 0, 0);
    public int TakeValue => Math.Clamp(Take ?? 50, 1, 200);
}
