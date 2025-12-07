namespace ResearchApi.Configuration;

public record ResearchOrchestratorConfig 
{
    public int LimitSearches { get; init; } = default!;
    public int MaxUrlParallelism { get; init; } = default!;
    public int MaxUrlsPerSerpQuery { get; init; } = default!;
};
