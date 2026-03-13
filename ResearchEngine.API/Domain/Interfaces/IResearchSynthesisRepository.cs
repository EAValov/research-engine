namespace ResearchEngine.Domain;

public interface IResearchSynthesisRepository
{
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

    Task<bool> DeleteSynthesisAsync(Guid synthesisId, CancellationToken ct = default);

    Task<Synthesis?> GetSynthesisAsync(Guid synthesisId, CancellationToken ct = default);

    Task<IReadOnlyList<SynthesisListItemDto>> ListSynthesesAsync(
        Guid jobId,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    Task<Synthesis?> GetLatestSynthesisAsync(Guid jobId, CancellationToken ct = default);
}
