using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Configuration;

public sealed record LearningSimilarityOptions
{
    /// <summary>
    /// Minimum importance score required when searching for similar learnings in the current job.
    /// </summary>
    [Range(0.0, 1.0)]
    public float MinImportance { get; init; } = 0.4f;

    /// <summary>
    /// Maximum number of returned learnings that may come from the same source URL.
    /// </summary>
    [Range(1, 1000)]
    public int DiversityMaxPerUrl { get; init; } = 3;

    /// <summary>
    /// Maximum allowed Jaccard similarity between learning texts during diversity filtering.
    /// </summary>
    [Range(0.0, 1.0)]
    public double DiversityMaxTextSimilarity { get; init; } = 0.85;

    /// <summary>
    /// Upper bound for the number of learnings extracted from a single content segment.
    /// </summary>
    [Range(1, 1000)]
    public int MaxLearningsPerSegment { get; init; } = 20;

    /// <summary>
    /// Lower bound for the number of learnings extracted from a single content segment.
    /// </summary>
    [Range(1, 1000)]
    public int MinLearningsPerSegment { get; init; } = 5;

    /// <summary>
    /// Minimum cosine-similarity score required to auto-assign a learning to an existing group.
    /// </summary>
    [Range(0.0, 1.0)]
    public float GroupAssignSimilarityThreshold { get; init; } = 0.93f;

    /// <summary>
    /// Number of nearest learning groups to inspect when assigning a learning to a group.
    /// </summary>
    [Range(1, 1000)]
    public int GroupSearchTopK { get; init; } = 5;

    /// <summary>
    /// Maximum number of characters stored as evidence text for a learning.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxEvidenceLength { get; init; } = 20_000;
}
