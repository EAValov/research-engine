namespace ResearchEngine.Domain;

public interface IResearchJobRepository
{
    Task<ResearchJob> CreateJobAsync(
        string query,
        IEnumerable<Clarification> clarifications,
        int breadth,
        int depth,
        string language,
        string? region,
        CancellationToken ct = default);

    Task<ResearchJob?> GetJobAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ResearchJob>> ListJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ResearchJob>> ListArchivedJobsAsync(CancellationToken ct = default);

    Task<int> UpdateJobAsync(ResearchJob job, CancellationToken ct = default);

    Task SetJobHangfireIdAsync(Guid jobId, string hangfireJobId, CancellationToken ct = default);

    Task RequestJobCancelAsync(Guid jobId, string? reason, CancellationToken ct = default);

    Task<int> ClearJobCancelRequestAsync(Guid jobId, CancellationToken ct = default);

    Task<bool> IsJobCancelRequestedAsync(Guid jobId, CancellationToken ct = default);

    Task<int> ArchiveJobAsync(Guid jobId, CancellationToken ct = default);
    Task<int> UnarchiveJobAsync(Guid jobId, CancellationToken ct = default);

    Task<int> SoftDeleteJobAsync(Guid jobId, string? reason, CancellationToken ct = default);

    Task<bool> IsJobDeletedAsync(Guid jobId, CancellationToken ct = default);
}
