namespace ResearchEngine.Domain;
public enum SynthesisStatus
{
    Created,
    Running,
    Completed,
    Failed
}

public sealed class Synthesis
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    // Lineage/versioning
    public Guid? ParentSynthesisId { get; set; }
    public Synthesis? ParentSynthesis { get; set; }
    public ICollection<Synthesis> Children { get; set; } = new List<Synthesis>();

    public SynthesisStatus Status { get; set; } = SynthesisStatus.Created;

    // Optional structured outline (markdown or JSON)
    public string? Outline { get; set; }

    // Extra system instructions appended to synthesis prompt
    public string? Instructions { get; set; }

    // NEW: stored sections for this synthesis (ordered by Index)
    public ICollection<SynthesisSection> Sections { get; set; } = new List<SynthesisSection>();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SynthesisSection
{
    public Guid Id { get; set; }

    public Guid SynthesisId { get; set; }
    public Synthesis Synthesis { get; set; } = null!;

    /// <summary>
    /// Stable section identity across syntheses in a lineage.
    /// Reused/updated sections should keep the same key.
    /// </summary>
    public Guid SectionKey { get; set; }

    /// <summary>
    /// Order within the synthesis (0..N-1).
    /// </summary>
    public int Index { get; set; }

    public string Title { get; set; } = null!;
    public string Description { get; set; } = string.Empty;

    public bool IsConclusion { get; set; }

    /// <summary>
    /// Markdown body for this section.
    /// </summary>
    public string ContentMarkdown { get; set; } = null!;

    /// <summary>
    /// Optional short summary used internally (planning/conclusion prompt, etc.).
    /// </summary>
    public string? Summary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record SynthesisOverridesSnapshot(
    Guid SynthesisId,
    Guid JobId,
    IReadOnlyDictionary<Guid, SynthesisSourceOverrideDto> SourceOverridesBySourceId,
    IReadOnlyDictionary<Guid, SynthesisLearningOverrideDto> LearningOverridesByLearningId);

public sealed record SynthesisListItemDto(
    Guid SynthesisId,
    Guid JobId,
    Guid? ParentSynthesisId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    int SectionCount
);
