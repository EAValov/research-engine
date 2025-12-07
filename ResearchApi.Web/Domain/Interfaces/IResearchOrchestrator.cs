namespace ResearchApi.Domain;

public interface IResearchOrchestrator
{
    Task<ResearchJob> StartJobAsync(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region, CancellationToken ct = default);
    Task RunJobAsync(Guid jobId, CancellationToken ct);
}
