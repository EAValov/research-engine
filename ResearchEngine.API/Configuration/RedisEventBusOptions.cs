using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Configuration;

public sealed record RedisEventBusOptions
{
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = default!;
}