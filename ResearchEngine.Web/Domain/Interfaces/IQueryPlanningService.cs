namespace ResearchEngine.Domain;
public interface IQueryPlanningService
{
    Task<IReadOnlyList<string>> GenerateSerpQueriesAsync(
        string query,
        string clarificationsText,
        int depth,
        int breadth,
        string targetLanguage,
        CancellationToken ct);
}
