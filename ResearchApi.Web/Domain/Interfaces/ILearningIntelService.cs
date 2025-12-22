namespace ResearchApi.Domain;

public interface ILearningIntelService
{
    /// Extracts learnings from source content (segmented), returns persist-ready entities
    /// with EvidenceText and optional Embedding attached (if computeEmbeddings == true).
    Task<IReadOnlyList<Learning>> ExtractLearningsAsync(
        Guid jobId,
        Guid sourceId,
        string query,
        string clarificationsText,
        string sourceUrl,
        string sourceContent,
        string queryHash,
        string targetLanguage,
        bool computeEmbeddings,
        CancellationToken ct);

    /// Computes embeddings for learnings that don't have them yet (in-memory only).
    Task<IReadOnlyList<Learning>> PopulateEmbeddingsAsync(
        IEnumerable<Learning> learnings,
        CancellationToken ct = default);

    /// Finds similar learnings using vector similarity (delegates DB search to store).
    Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid synthesisId,
        string? queryHash = null,
        string? language = null,
        string? region = null,
        int topK = 20,
        CancellationToken ct = default);
}
