using Pgvector;

namespace ResearchApi.Domain;

public sealed class Learning
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public Guid SourceId { get; set; }
    public Source Source { get; set; } = null!;

    // Optional grouping key you already used
    public string QueryHash { get; set; } = null!;

    public string Text { get; set; } = null!;

    // The score assigned by the model during extraction (keep your current meaning)
    public float ImportanceScore { get; set; }

    // Evidence/provenance for auditability
    public string EvidenceText { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTime.UtcNow;

    public LearningEmbedding Embedding { get; set; } = null!;
}

public sealed class LearningEmbedding
{
    public Guid Id { get; set; }

    public Guid LearningId { get; set; }
    public Learning Learning { get; set; } = null!;

    public Vector Vector { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record LearningListItemDto(
    Guid LearningId,
    Guid SourceId,
    string SourceUrl,
    float ImportanceScore,
    DateTimeOffset CreatedAt,
    string Text);
