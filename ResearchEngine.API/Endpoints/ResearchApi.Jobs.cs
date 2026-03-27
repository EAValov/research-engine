using Hangfire;
using Microsoft.AspNetCore.Mvc;
using ResearchEngine.Domain;

namespace ResearchEngine.API;

public static partial class ResearchApi
{
    /// <summary>
    /// POST /api/research/jobs
    /// Creates a research job and enqueues the initial deep-research run.
    /// </summary>
    private static async Task<IResult> CreateJobAsync(
        [FromBody] CreateResearchJobRequest request,
        IResearchOrchestrator orchestrator,
        IResearchProtocolService protocolService,
        CancellationToken ct)
    {
        int? breadth = request.Breadth;
        int? depth = request.Depth;
        string? language = request.Language;
        string? region = request.Region;

        var clarifications = request.Clarifications?.Select(c => new Clarification
        {
            Question = c.Question,
            Answer = c.Answer
        }).ToList() ?? new();

        if (!breadth.HasValue || !depth.HasValue)
            (breadth, depth) =
                await protocolService.AutoSelectBreadthDepthAsync(request.Query, clarifications, ct);

        if (string.IsNullOrEmpty(language))
            (language, region) =
                await protocolService.AutoSelectLanguageRegionAsync(request.Query, clarifications, ct);

        SourceDiscoveryMode? discoveryMode = null;
        if (!string.IsNullOrWhiteSpace(request.DiscoveryMode))
        {
            if (!SourceDiscoveryModeExtensions.TryParse(request.DiscoveryMode, out var parsedMode))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(CreateResearchJobRequest.DiscoveryMode)] = ["DiscoveryMode must be Balanced, ReliableOnly, or AcademicOnly."]
                });
            }

            discoveryMode = parsedMode;
        }

        var jobId = await orchestrator.StartJobAsync(
            request.Query,
            clarifications,
            breadth ?? 2,
            depth ?? 2,
            discoveryMode,
            language ?? "en",
            region,
            ct);

        return Results.Created($"/api/jobs/{jobId}", new CreateResearchJobResponse(jobId));
    }

    /// <summary>
    /// GET /api/research/jobs
    /// Lists jobs for the UX sidebar.
    /// </summary>
    private static async Task<IResult> ListJobsAsync(
        [FromQuery] bool archived,
        IResearchJobRepository jobRepository,
        CancellationToken ct)
    {
        var jobs = archived
            ? await jobRepository.ListArchivedJobsAsync(ct)
            : await jobRepository.ListJobsAsync(ct);

        var items = jobs.Select(j => new ResearchJobListItemDto(
            Id: j.Id,
            Query: j.Query,
            ChatModelName: j.ChatModelName,
            EmbeddingModelName: j.EmbeddingModelName,
            Breadth: j.Breadth,
            Depth: j.Depth,
            DiscoveryMode: j.DiscoveryMode.ToApiValue(),
            Status: j.Status.ToString(),
            TargetLanguage: j.TargetLanguage,
            Region: j.Region,
            ArchivedAt: j.ArchivedAt,
            CreatedAt: j.CreatedAt,
            UpdatedAt: j.UpdatedAt
        )).ToList();

        return Results.Ok(new ListResearchJobsResponse(items.Count, items));
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}
    /// Returns job details + counts + latest synthesis summary.
    /// </summary>
    private static async Task<IResult> GetJobAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var latest = job.Syntheses?
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

        return Results.Ok(new GetResearchJobResponse(
            Id: job.Id,
            Query: job.Query,
            ChatModelName: job.ChatModelName,
            EmbeddingModelName: job.EmbeddingModelName,
            Breadth: job.Breadth,
            Depth: job.Depth,
            DiscoveryMode: job.DiscoveryMode.ToApiValue(),
            Status: job.Status.ToString(),
            TargetLanguage: job.TargetLanguage,
            Region: job.Region,
            ArchivedAt: job.ArchivedAt,
            CreatedAt: job.CreatedAt,
            UpdatedAt: job.UpdatedAt,
            Clarifications: job.Clarifications
                .Select(c => new ClarificationDto(c.Question, c.Answer))
                .ToList(),
            SourcesCount: job.Sources.Count,
            SynthesesCount: job.Syntheses?.Count ?? 0,
            LatestSynthesis: latest is null
                ? null
                : new LatestSynthesisSummaryDto(
                    latest.Id,
                    latest.Status.ToString(),
                    latest.ChatModelName,
                    latest.EmbeddingModelName,
                    latest.CreatedAt,
                    latest.CompletedAt)
            ));
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/archive
    /// Archives a job, hiding it from the default recent jobs list.
    /// </summary>
    private static async Task<IResult> ArchiveJobAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null) return Results.NotFound();

        var actionStage = MapUserActionStage(job.Status);

        await jobRepository.ArchiveJobAsync(jobId, ct);

        await eventRepository.AppendEventAsync(
            jobId,
            new ResearchEvent(
                DateTimeOffset.UtcNow,
                actionStage,
                "Job archived by user"
            ),
            ct);

        return Results.NoContent();
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/unarchive
    /// Restores a job back to the default recent jobs list.
    /// </summary>
    private static async Task<IResult> UnarchiveJobAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null) return Results.NotFound();

        var actionStage = MapUserActionStage(job.Status);

        await jobRepository.UnarchiveJobAsync(jobId, ct);

        await eventRepository.AppendEventAsync(
            jobId,
            new ResearchEvent(
                DateTimeOffset.UtcNow,
                actionStage,
                "Job unarchived by user"
            ),
            ct);

        return Results.NoContent();
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/cancel
    /// Requests job cancellation (best-effort) and deletes queued Hangfire job if not yet running.
    /// </summary>
    private static async Task<IResult> CancelJobAsync(
        Guid jobId,
        [FromBody] CancelJobRequest? request,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null) return Results.NotFound();

        await jobRepository.RequestJobCancelAsync(jobId, request?.Reason, ct);

        await eventRepository.AppendEventAsync(
            jobId,
            new ResearchEvent(
                DateTimeOffset.UtcNow,
                ResearchEventStage.Canceled,
                $"Cancel requested{(string.IsNullOrWhiteSpace(request?.Reason) ? "" : $": {request!.Reason}")}"
            ),
            ct);

        if (!string.IsNullOrWhiteSpace(job.HangfireJobId))
            backgroundJobs.Delete(job.HangfireJobId);

        return Results.Accepted(null, new CancelJobResponse(jobId, "cancel_requested"));
    }

    /// <summary>
    /// DELETE /api/research/jobs/{jobId}
    /// Soft-deletes a job (and prevents queued Hangfire job from starting).
    /// </summary>
    private static async Task<IResult> SoftDeleteJobAsync(
        Guid jobId,
        [FromBody] DeleteJobRequest? request,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null) return Results.NotFound();

        var actionStage = MapUserActionStage(job.Status);

        if (!string.IsNullOrWhiteSpace(job.HangfireJobId))
            backgroundJobs.Delete(job.HangfireJobId);

        await jobRepository.SoftDeleteJobAsync(jobId, request?.Reason, ct);

        await eventRepository.AppendEventAsync(
            jobId,
            new ResearchEvent(
                DateTimeOffset.UtcNow,
                actionStage,
                $"Job deleted (soft){(string.IsNullOrWhiteSpace(request?.Reason) ? "" : $": {request!.Reason}")}"
            ),
            ct);

        return Results.NoContent();
    }

    private static ResearchEventStage MapUserActionStage(ResearchJobStatus status)
        => status switch
        {
            ResearchJobStatus.Completed => ResearchEventStage.Completed,
            ResearchJobStatus.Failed => ResearchEventStage.Failed,
            ResearchJobStatus.Canceled => ResearchEventStage.Canceled,
            _ => ResearchEventStage.Planning
        };
}
