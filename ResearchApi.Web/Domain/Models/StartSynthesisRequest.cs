using System.ComponentModel.DataAnnotations;
using ResearchApi.Domain;

public sealed class StartSynthesisRequest
{
    /// <summary>
    /// Explicit parent synthesis id. If null, you may set UseLatestAsParent=true.
    /// </summary>
    public Guid? ParentSynthesisId { get; init; }

    /// <summary>
    /// If true and ParentSynthesisId is null, will use the latest synthesis as parent (if any).
    /// </summary>
    public bool? UseLatestAsParent { get; init; }

    /// <summary>
    /// Optional outline to guide section planning + writing.
    /// </summary>
    [MaxLength(10_000)]
    public string? Outline { get; init; }

    /// <summary>
    /// Optional extra instructions to guide synthesis tone/constraints.
    /// </summary>
    [MaxLength(10_000)]
    public string? Instructions { get; init; }

    /// <summary>
    /// Optional source-level overrides for this synthesis run.
    /// </summary>
    [MaxLength(10_000)]
    public IReadOnlyList<SynthesisSourceOverrideDto>? SourceOverrides { get; init; }

    /// <summary>
    /// Optional learning-level overrides for this synthesis run.
    /// </summary>
    [MaxLength(50_000)]
    public IReadOnlyList<SynthesisLearningOverrideDto>? LearningOverrides { get; init; }
}
