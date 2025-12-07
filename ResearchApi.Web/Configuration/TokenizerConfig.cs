namespace ResearchApi.Infrastructure;

public record TokenizerConfig
{
    public string BaseUrl { get; init; } = default!;
    public string? ModelId { get; init; }
}