namespace ResearchEngine.Domain;

public interface IResearchProtocolService
{
    Task<IReadOnlyList<string>> GenerateFeedbackQueriesAsync(string query, CancellationToken ct = default);
    Task<(int breadth, int depth)> AutoSelectBreadthDepthAsync(string query, IReadOnlyList<Clarification> clarifications, CancellationToken ct = default);
    Task<(string language, string? region)> AutoSelectLanguageRegionAsync(string query, IReadOnlyList<Clarification> clarifications, CancellationToken ct = default);
    Task<SourceDiscoveryMode> AutoSelectDiscoveryModeAsync(string query, IReadOnlyList<Clarification> clarifications, CancellationToken ct = default);
}
