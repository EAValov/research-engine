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

        var jobId = await orchestrator.StartJobAsync(
            request.Query,
            clarifications,
            breadth ?? 2,
            depth ?? 2,
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
        IResearchJobRepository jobRepository,
        CancellationToken ct)
    {
        var jobs = await jobRepository.ListJobsAsync(ct);

        var items = jobs.Select(j => new ResearchJobListItemDto(
            Id: j.Id,
            Query: j.Query,
            ChatModelName: j.ChatModelName,
            EmbeddingModelName: j.EmbeddingModelName,
            Breadth: j.Breadth,
            Depth: j.Depth,
            Status: j.Status.ToString(),
            TargetLanguage: j.TargetLanguage,
            Region: j.Region,
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
            Status: job.Status.ToString(),
            TargetLanguage: job.TargetLanguage,
            Region: job.Region,
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

        if (!string.IsNullOrWhiteSpace(job.HangfireJobId))
            backgroundJobs.Delete(job.HangfireJobId);

        await jobRepository.SoftDeleteJobAsync(jobId, request?.Reason, ct);

        await eventRepository.AppendEventAsync(
            jobId,
            new ResearchEvent(
                DateTimeOffset.UtcNow,
                ResearchEventStage.Planning,
                $"Job deleted (soft){(string.IsNullOrWhiteSpace(request?.Reason) ? "" : $": {request!.Reason}")}"
            ),
            ct);

        return Results.NoContent();
    }
}
