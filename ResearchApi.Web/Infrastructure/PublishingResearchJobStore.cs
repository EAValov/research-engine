using ResearchApi.Domain;

namespace ResearchApi.Infrastructure;

public class PublishingResearchJobStore : IResearchJobStore
{
    private readonly IResearchJobStore _innerStore;
    private readonly IResearchEventBus _eventBus;

    public PublishingResearchJobStore(
        IResearchJobStore innerStore,
        IResearchEventBus eventBus)
    {
        _innerStore = innerStore;
        _eventBus = eventBus;
    }

    public async Task<int> AppendEventAsync(Guid jobId, ResearchEvent ev, CancellationToken ct)
    {
        // First append the event to the store
        var result = await _innerStore.AppendEventAsync(jobId, ev, ct);
        
        // Then publish it via Redis
        await _eventBus.PublishAsync(jobId, ev, ct);
        
        return result;
    }

    public async Task<ResearchJob> CreateJobAsync(string query, IEnumerable<Clarification> clarifications, int breadth, int depth, string language, string? region, CancellationToken ct)
    {
        var job = await _innerStore.CreateJobAsync(query, clarifications, breadth, depth, language, region, ct);
        return job;
    }

    public async Task<IReadOnlyList<ResearchEvent>> GetEventsAsync(Guid jobId, CancellationToken ct)
    {
        return await _innerStore.GetEventsAsync(jobId, ct);
    }

    public async Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct)
    {
        return await _innerStore.GetJobAsync(id, ct);
    }

    public async Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct)
    {
        var result = await _innerStore.UpdateJobAsync(job, ct);
        return result;
    }
}
