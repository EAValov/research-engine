using System.ComponentModel.DataAnnotations;
namespace ResearchEngine.API;

public sealed class CancelJobRequest
{
    [MaxLength(2000)]
    public string? Reason { get; init; }
}
