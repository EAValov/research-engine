using System.ComponentModel.DataAnnotations;

namespace ResearchApi.Domain;

public sealed record RedisEventBusOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = default!;

    /// <summary>
    /// Approximate max length per job stream (XADD MAXLEN ~).
    /// Prevents unbounded growth.
    /// </summary>
    [Range(100, 5_000_000)]
    public int StreamMaxLen { get; init; } = 10_000;

    /// <summary>
    /// XREAD BLOCK duration in milliseconds.
    /// </summary>
    [Range(50, 60_000)]
    public int BlockMs { get; init; } = 1_000;

    /// <summary>
    /// If no entries arrive, we loop again. This is just a safety delay.
    /// </summary>
    [Range(0, 5_000)]
    public int IdleDelayMs { get; init; } = 50;
}