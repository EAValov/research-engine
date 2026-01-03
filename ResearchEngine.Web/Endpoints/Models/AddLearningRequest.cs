using System.ComponentModel.DataAnnotations;

namespace ResearchEngine.Web;

public sealed class AddLearningRequest
{
    /// <summary>Required. The learning statement/claim.</summary>
    [Required]
    [MinLength(3)]
    [MaxLength(10_000)]
    public string Text { get; init; } = null!;

    /// <summary>
    /// Optional reference (URL or citation string, e.g. "Smith 2020, pp. 12-15").
    /// If omitted, learning is stored under a per-job "user:manual" source.
    /// </summary>
    [MaxLength(4000)]
    public string? Reference { get; init; }

    /// <summary>Optional evidence/notes to justify the learning (kept for auditability).</summary>
    [MaxLength(20_000)]
    public string? EvidenceText { get; init; }

    /// <summary>Optional score (0..1). Defaults to 1.0.</summary>
    [Range(0.0, 1.0)]
    public float? ImportanceScore { get; init; }

    /// <summary>Optional language metadata for the reference source (if provided).</summary>
    [MaxLength(20)]
    public string? Language { get; init; }

    /// <summary>Optional region metadata for the reference source (if provided).</summary>
    [MaxLength(500)]
    public string? Region { get; init; }
}