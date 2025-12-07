namespace ResearchApi.Domain;

public interface IResearchJobStore
{
    Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default);
    Task<ResearchJob> CreateJobAsync(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default);
    Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default);
    Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default);
}
