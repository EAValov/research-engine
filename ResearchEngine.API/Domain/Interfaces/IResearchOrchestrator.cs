namespace ResearchEngine.Domain;

public interface IResearchOrchestrator
{
    /// <summary>
    /// Creates a job row and immediately starts running it in the background.
    /// Returns the job id.
    /// </summary>
    Task<Guid> StartJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        SourceDiscoveryMode? discoveryMode,
        string language,
        string? region,
        CancellationToken ct = default);

    /// <summary>
    /// Worker entrypoint (used by background execution / tests).
    /// Wrapper for Hangfire invocation
    /// </summary>
    public Task RunJobBackgroundAsync(Guid jobId);
}
