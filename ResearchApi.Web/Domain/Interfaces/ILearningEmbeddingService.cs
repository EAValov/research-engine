namespace ResearchApi.Domain;

public interface ILearningEmbeddingService
{
    /// <summary>
    /// Ensures that all given learnings have embeddings.
    /// Only computes embeddings for learnings with Embedding == null.
    /// </summary>
    Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default);

    Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid? jobId          = null,
        string? queryHash    = null,
        string? language     = null,
        string? region       = null,
        int topK             = 20,
        CancellationToken ct = default);
}
