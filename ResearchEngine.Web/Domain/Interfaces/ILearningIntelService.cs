namespace ResearchEngine.Domain;

public interface ILearningIntelService
{
    /// Extracts learnings from source content (segmented), returns persist-ready entities
    /// with EvidenceText and optional Embedding attached (if computeEmbeddings == true).
    Task<IReadOnlyList<Learning>> ExtractAndSaveLearningsAsync(
        Guid jobId,
        Guid sourceId,
        string query,
        string clarificationsText,
        string sourceUrl,
        string sourceContent,
        string targetLanguage,
        bool computeEmbeddings,
        CancellationToken ct);

    /// Finds similar learnings using vector similarity (delegates DB search to store).
    Task<IReadOnlyList<Learning>> GetSimilarLearningsAsync(
        string queryText,
        Guid synthesisId,
        string? language = null,
        string? region = null,
        int topK = 20,
        CancellationToken ct = default);

    Task<Learning> AddUserLearningAsync(
        Guid jobId,
        string text,
        float importanceScore = 1.0f,
        string? reference = null,
        string? evidenceText = null,
        string? language = null,
        string? region = null,
        CancellationToken ct = default);
}
