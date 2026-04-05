using Pgvector;

namespace ResearchEngine.Domain;

public sealed class Learning
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;
    public Guid SourceId { get; set; }
    public Source Source { get; set; } = null!;

    public Guid LearningGroupId { get; set; }
    public LearningGroup Group { get; set; } = null!;

    /// <summary>
    /// True for user-added learnings. We never delete these automatically when source content changes.
    /// </summary>
    public bool IsUserProvided { get; set; }

    // Optional grouping key
    public string QueryHash { get; set; } = null!;

    public string Text { get; set; } = null!;
    public LearningStatementType StatementType { get; set; } = LearningStatementType.Finding;
    public float ImportanceScore { get; set; }
    public string EvidenceText { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DeletedAt { get; set; }

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
    string SourceReference,
    float ImportanceScore,
    DateTimeOffset CreatedAt,
    string Text);

