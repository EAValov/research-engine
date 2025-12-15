using System.ComponentModel.DataAnnotations;

public sealed record ClarificationDto(
    [Required] string Question,
    [Required] string Answer
);
