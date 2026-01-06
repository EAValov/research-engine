
using Pgvector;

namespace ResearchEngine.Domain;

public interface IResearchJobStore
{
    Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default);

    Task<ResearchJob> CreateJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        string language,
        string? region,
        CancellationToken ct = default);

    Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default);
    
    Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default);

    Task<Source> UpsertSourceAsync(
        Guid jobId,
        string reference,
        string content,
        string? title,
        string? language,
        string? region,
        SourceKind kind,
        CancellationToken ct = default);

    Task<Synthesis> CreateSynthesisAsync(
        Guid jobId,
        Guid? parentSynthesisId,
        string? outline,
        string? instructions,
        CancellationToken ct = default);

    Task<int> MarkSynthesisRunningAsync(Guid synthesisId, CancellationToken ct = default);

    Task<int> CompleteSynthesisAsync(
        Guid synthesisId,
        IReadOnlyList<SynthesisSection> sections,
        CancellationToken ct = default);

    Task<int> FailSynthesisAsync(
        Guid synthesisId,
        string errorMessage,
        CancellationToken ct = default);

    Task<Synthesis?> GetSynthesisAsync(Guid synthesisId, CancellationToken ct = default);

    Task<Synthesis?> GetLatestSynthesisAsync(Guid jobId, CancellationToken ct = default);

    Task<IReadOnlyList<Learning>> GetLearningsForSourceAndQueryAsync(Guid sourceId, string serpQuery, CancellationToken ct = default);

    Task AddLearningsAsync(
        Guid jobId,
        Guid sourceId,
        string query,
        IEnumerable<Learning> learnings,
        CancellationToken ct = default);

    Task<LearningGroupCardDto?> GetLearningGroupCardByLearningIdAsync(Guid learningId, CancellationToken ct = default);
    Task<IReadOnlyList<ResolvedLearningGroupDto>> ResolveLearningGroupsBatchAsync(IReadOnlyList<Guid> learningIds, CancellationToken ct = default);

    Task<bool> SoftDeleteLearningAsync(Guid jobId, Guid learningId, CancellationToken ct = default);
    Task<bool> SoftDeleteSourceAsync(Guid jobId, Guid sourceId, CancellationToken ct = default);

    Task<IReadOnlyList<SourceListItemDto>> ListSourcesAsync(Guid jobId, CancellationToken ct = default);

    Task<PagedResult<LearningListItemDto>> ListLearningsAsync(
        Guid jobId,
        int skip = 0,
        int take = 200,
        CancellationToken ct = default);

    // Overrides persistence
    Task AddOrUpdateSynthesisSourceOverridesAsync(
        Guid synthesisId,
        IEnumerable<SynthesisSourceOverrideDto> overrides,
        CancellationToken ct = default);

    Task AddOrUpdateSynthesisLearningOverridesAsync(
        Guid synthesisId,
        IEnumerable<SynthesisLearningOverrideDto> overrides,
        CancellationToken ct = default);

    Task<SynthesisOverridesSnapshot> GetSynthesisOverridesAsync(
        Guid synthesisId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Learning>> VectorSearchLearningsAsync(
        Vector queryVector,
        Guid? jobId,
        string? language,
        string? region,
        float minImportance,
        int topK,
        CancellationToken ct = default);

    Task<IReadOnlyList<LearningGroup>> VectorSearchLearningGroupsAsync(
        Vector queryVector,
        Guid jobId,
        int topK,
        CancellationToken ct = default);
    
    Task<LearningGroup> CreateLearningGroupAsync(
        Guid jobId,
        string canonicalText,
        float canonicalImportanceScore,
        ReadOnlyMemory<float> embeddingVector,
        CancellationToken ct = default);

    Task<int> UpdateLearningGroupCanonicalAsync(
        Guid groupId,
        string canonicalText,
        float canonicalImportanceScore,
        ReadOnlyMemory<float> embeddingVector,
        CancellationToken ct = default);

    Task<int> RecomputeLearningGroupStatsAsync(Guid groupId, CancellationToken ct = default);

    Task<IReadOnlyList<LearningGroupHit>> VectorSearchLearningGroupsWithDistanceAsync(
        Vector queryVector,
        Guid jobId,
        int topK,
        CancellationToken ct = default);

    Task<Source> GetOrCreateUserSourceAsync(Guid jobId, CancellationToken ct = default);
}
