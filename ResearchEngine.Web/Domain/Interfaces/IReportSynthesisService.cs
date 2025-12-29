using ResearchEngine.Application;

namespace ResearchEngine.Domain;

public interface IReportSynthesisService
{
    /// <summary>
    /// Creates a synthesis row and returns its id. No long-running work here.
    /// </summary>
    Task<Guid> StartSynthesisAsync(
        Guid jobId,
        Guid? parentSynthesisId,
        string? outline,
        string? instructions,
        CancellationToken ct);

    /// <summary>
    /// Runs an already-created synthesis row by id (long-running),
    /// updates status + report markdown in DB.
    /// </summary>
    Task RunExistingSynthesisAsync(
        Guid synthesisId,
        ResearchProgressTracker? progress,
        CancellationToken ct);
}
