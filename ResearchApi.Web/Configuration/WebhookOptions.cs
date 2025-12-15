using System.ComponentModel.DataAnnotations;

public sealed record WebhookOptions
{
    [Required(AllowEmptyStrings = false)]
    public string RedisConnectionString { get; init; } = default!;

    [Range(1, 20)]
    public int RetryMaxAttempts { get; init; } = 5;

    [Range(1, 60)]
    public int RetryBaseDelaySeconds { get; init; } = 2;

    [Range(1, 120)]
    public int HttpTimeoutSeconds { get; init; } = 15;
}