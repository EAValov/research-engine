using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Configuration;

public sealed record ResearchOrchestratorConfig 
{
    /// <summary>
    /// Maximum number of search results requested for each SERP query.
    /// </summary>
    [Range(1, 1000)]
    public int LimitSearches { get; init; } = default!;

    /// <summary>
    /// Maximum number of source URLs processed in parallel for one SERP query.
    /// </summary>
    [Range(1, 1000)]
    public int MaxUrlParallelism { get; init; } = default!;

    /// <summary>
    /// Maximum number of unique URLs processed from a SERP query.
    /// </summary>
    [Range(1, 1000)]
    public int MaxUrlsPerSerpQuery { get; init; } = default!;
};
