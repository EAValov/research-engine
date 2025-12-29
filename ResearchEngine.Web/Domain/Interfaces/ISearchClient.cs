namespace ResearchEngine.Domain;
public interface ISearchClient
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
    string query,
    int limit,
    string? location = null,
    CancellationToken ct = default);
}
