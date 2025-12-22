namespace ResearchApi.Domain;

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

    // For lineage/versioning of syntheses
    public Guid? ParentSynthesisId { get; set; }
    public Synthesis? ParentSynthesis { get; set; }
    public ICollection<Synthesis> Children { get; set; } = new List<Synthesis>();

    public SynthesisStatus Status { get; set; } = SynthesisStatus.Created;

    // Optional structured outline (markdown or JSON)
    public string? Outline { get; set; }

    // Extra system instructions appended to synthesis prompt
    public string? Instructions { get; set; }

    // Final report
    public string? ReportMarkdown { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed record SynthesisOverridesSnapshot(
    Guid SynthesisId,
    Guid JobId,
    IReadOnlyDictionary<Guid, SynthesisSourceOverrideDto> SourceOverridesBySourceId,
    IReadOnlyDictionary<Guid, SynthesisLearningOverrideDto> LearningOverridesByLearningId);