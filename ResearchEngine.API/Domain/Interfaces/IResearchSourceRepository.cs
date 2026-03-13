namespace ResearchEngine.Domain;

public interface IResearchSourceRepository
{
    Task<Source> UpsertSourceAsync(
        Guid jobId,
        string reference,
        string content,
        string? title,
        string? language,
        string? region,
        SourceKind kind,
        CancellationToken ct = default);

    Task<bool> SoftDeleteSourceAsync(Guid jobId, Guid sourceId, CancellationToken ct = default);

    Task<IReadOnlyList<SourceListItemDto>> ListSourcesAsync(Guid jobId, CancellationToken ct = default);

    Task<Source> GetOrCreateUserSourceAsync(Guid jobId, CancellationToken ct = default);
}
