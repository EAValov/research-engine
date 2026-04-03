namespace ResearchEngine.API;

public interface IReleaseUpdateService
{
    Task<UpdateStatusResponse> GetStatusAsync(CancellationToken ct = default);
}
