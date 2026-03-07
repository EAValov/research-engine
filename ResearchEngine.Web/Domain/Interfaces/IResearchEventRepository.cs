namespace ResearchEngine.Domain;

public interface IResearchEventRepository
{
    Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct = default);
    Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct = default);
}
