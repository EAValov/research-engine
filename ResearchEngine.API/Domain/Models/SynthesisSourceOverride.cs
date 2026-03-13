using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Domain;

public sealed class SynthesisSourceOverride
{
    public Guid Id { get; set; }

    public Guid SynthesisId { get; set; }
    public Synthesis Synthesis { get; set; } = null!;

    public Guid SourceId { get; set; }
    public Source Source { get; set; } = null!;

    public bool? Excluded { get; set; }                // if true => don't use in retrieval
    public bool? Pinned { get; set; }                  // optional: priority boost

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record SynthesisSourceOverrideDto(
    [Required] Guid SourceId,
    bool? Excluded,
    bool? Pinned);
