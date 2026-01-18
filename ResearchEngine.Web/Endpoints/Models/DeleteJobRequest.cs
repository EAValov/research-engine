using System.ComponentModel.DataAnnotations;
namespace ResearchEngine.Web;

public sealed class DeleteJobRequest
{
    [MaxLength(2000)]
    public string? Reason { get; init; }
}
