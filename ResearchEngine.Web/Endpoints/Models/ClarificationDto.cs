using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Web;

public sealed record ClarificationDto(
    [Required] string Question,
    [Required] string Answer
);
