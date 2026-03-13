using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.API;

public sealed record ClarificationDto(
    [Required] string Question,
    [Required] string Answer
);
