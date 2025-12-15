using System.ComponentModel.DataAnnotations;

public sealed record WebhookDto(
    [Required]
    [Url]
    string Url,
    string? Secret
);
