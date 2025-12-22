using System.ComponentModel.DataAnnotations;

namespace ResearchApi.Domain;

public sealed class SynthesisLearningOverride
{
    public Guid Id { get; set; }

    public Guid SynthesisId { get; set; }
    public Synthesis Synthesis { get; set; } = null!;

    public Guid LearningId { get; set; }
    public Learning Learning { get; set; } = null!;

    public float? ScoreOverride { get; set; }          // optional per-learning adjustment
    public bool? Excluded { get; set; }
    public bool? Pinned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record SynthesisLearningOverrideDto(
    [Required] Guid LearningId,
    float? ScoreOverride,
    bool? Excluded,
    bool? Pinned);