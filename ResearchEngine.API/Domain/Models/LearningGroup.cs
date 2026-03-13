using Pgvector;

namespace ResearchEngine.Domain;

public sealed class LearningGroup
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }
    public ResearchJob Job { get; set; } = null!;

    public string CanonicalText { get; set; } = null!;

    /// <summary>
    /// Importance of the canonical claim (we update if a higher-importance learning becomes representative).
    /// </summary>
    public float CanonicalImportanceScore { get; set; }

    public int MemberCount { get; set; }
    public int DistinctSourceCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public LearningGroupEmbedding Embedding { get; set; } = null!;
    public ICollection<Learning> Learnings { get; set; } = new List<Learning>();
}

public sealed class LearningGroupEmbedding
{
    public Guid Id { get; set; }

    public Guid LearningGroupId { get; set; }
    public LearningGroup Group { get; set; } = null!;

    public Vector Vector { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record LearningGroupCardDto(
    Guid GroupId,
    Guid JobId,
    string CanonicalText,
    float CanonicalImportanceScore,
    int MemberCount,
    int DistinctSourceCount,
    Guid RepresentativeLearningId,
    string RepresentativeLearningText,
    IReadOnlyList<GroupEvidenceItemDto> Evidence);

public sealed record GroupEvidenceItemDto(
    Guid LearningId,
    Guid SourceId,
    string SourceReference,
    float ImportanceScore,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record ResolvedLearningGroupDto(
    Guid LearningId,
    LearningGroupCardDto? Group); // null if not found/deleted