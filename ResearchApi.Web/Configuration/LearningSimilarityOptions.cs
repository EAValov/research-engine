using System.ComponentModel.DataAnnotations;

namespace ResearchApi.Configuration;

public sealed class LearningSimilarityOptions
{
    // When searching in the current job only
    [Range(0.0, 1.0)]
    public float LocalMinImportance { get; init; } = 0.4f;

    // When pulling in "global" learnings from other jobs
    [Range(0.0, 1.0)]
    public float GlobalMinImportance { get; init; } = 0.65f;

    // If we already have ≥ this fraction of topK from local,
    // we skip global search (0–1).
    [Range(0.0, 1.0)]
    public float MinLocalFractionForNoGlobal { get; init; } = 0.75f;

    // Diversity filter: max learnings from the same URL
    [Range(1, 1000)]
    public int DiversityMaxPerUrl { get; init; } = 3;

    // Diversity filter: max allowed Jaccard similarity between texts (0–1)
    [Range(0.0, 1.0)]
    public double DiversityMaxTextSimilarity { get; init; } = 0.85;
}