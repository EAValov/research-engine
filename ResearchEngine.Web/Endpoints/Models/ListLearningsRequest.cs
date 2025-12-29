using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Web;

public sealed class ListLearningsRequest
{
    // Default behavior if omitted
    public const int DefaultSkip = 0;
    public const int DefaultTake = 00;

    [Range(0, int.MaxValue, ErrorMessage = "skip must be >= 0")]
    public int? Skip { get; init; }

    [Range(1, 500, ErrorMessage = "take must be between 1 and 100")]
    public int? Take { get; init; }

    public int SkipValue => Skip ?? DefaultSkip;
    public int TakeValue => Take ?? DefaultTake;
}