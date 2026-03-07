using Pgvector;

namespace ResearchEngine.Domain;

public interface IResearchLearningRepository
{
    Task<IReadOnlyList<Learning>> GetLearningsForSourceAndQueryAsync(
        Guid sourceId,
        string serpQuery,
        CancellationToken ct = default);

    Task AddLearningsAsync(
        Guid jobId,
        Guid sourceId,
        string query,
        IEnumerable<Learning> learnings,
        CancellationToken ct = default);

    Task<LearningGroupCardDto?> GetLearningGroupCardByLearningIdAsync(
        Guid learningId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ResolvedLearningGroupDto>> ResolveLearningGroupsBatchAsync(
        IReadOnlyList<Guid> learningIds,
        CancellationToken ct = default);

    Task<bool> SoftDeleteLearningAsync(Guid jobId, Guid learningId, CancellationToken ct = default);

    Task<PagedResult<LearningListItemDto>> ListLearningsAsync(
        Guid jobId,
        int skip = 0,
        int take = 200,
        CancellationToken ct = default);

    Task<IReadOnlyList<Learning>> VectorSearchLearningsAsync(
        Vector queryVector,
        Guid? jobId,
        string? language,
        string? region,
        float minImportance,
        int topK,
        CancellationToken ct = default);
}
