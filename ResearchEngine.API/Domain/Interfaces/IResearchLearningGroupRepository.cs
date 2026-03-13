using Pgvector;

namespace ResearchEngine.Domain;

public interface IResearchLearningGroupRepository
{
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
}
