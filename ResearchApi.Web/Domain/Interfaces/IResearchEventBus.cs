namespace ResearchApi.Domain;

public interface IResearchEventBus
{
    Task PublishAsync(Guid jobId, ResearchEvent ev, CancellationToken ct);
    Task<IAsyncDisposable> SubscribeAsync(
        Guid jobId,
        Func<ResearchEvent, CancellationToken, Task> onEvent,
        CancellationToken ct);
}
