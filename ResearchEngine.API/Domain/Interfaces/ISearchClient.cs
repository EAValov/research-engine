namespace ResearchEngine.Domain;
public interface ISearchClient
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken ct = default);
}
