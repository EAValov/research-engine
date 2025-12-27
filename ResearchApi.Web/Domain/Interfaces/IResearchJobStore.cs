
using Pgvector;

namespace ResearchApi.Domain;

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
        string url,
        string content,
        string? title,
        string? language,
        string? region,
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

    // Override snapshot used by retrieval
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
}
