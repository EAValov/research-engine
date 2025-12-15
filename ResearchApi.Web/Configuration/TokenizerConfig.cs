namespace ResearchApi.Infrastructure;

public sealed record TokenizerConfig
{
    public string BaseUrl { get; init; } = default!;
    public string? ModelId { get; init; }
}