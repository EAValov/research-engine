using ResearchEngine.Application;

namespace ResearchEngine.Domain;

public interface IReportSynthesisService
{
    /// <summary>
    /// Creates a synthesis row and returns its id. No long-running work here.
    /// </summary>
    Task<Guid> CreateSynthesisAsync(
        Guid jobId,
        Guid? parentSynthesisId,
        string? outline,
        string? instructions,
        CancellationToken ct);

    /// <summary>
    /// Run exisitng synthesis.
    /// Used in the ResearchOrchestrator for the initial research run.
    /// Also for tests.
    /// </summary>
    Task RunSynthesisAsync(Guid synthesisId, ResearchProgressTracker? progress, CancellationToken ct);

    /// <summary>
    /// Enqueue the exiting synthesis run.
    /// Returns Hangfire job id.
    /// </summary>
    string EnqueueSynthesisRun(Guid synthesisId);

    /// <summary>
    /// Exposed for Hangfire.
    /// </summary>
    Task RunSynthesisBackgroundAsync(Guid synthesisId);
}
