using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using ResearchEngine.Domain;

namespace ResearchEngine.Web;

public static partial class ResearchApi
{
    public static void MapResearchApi(this WebApplication app)
    {
        MapRoutes(app.MapGroup("/api/research")
            .WithTags("Research Jobs API")
            .RequireAuthorization());

        MapRoutes(app.MapGroup("/api")
            .WithTags("Research API")
            .RequireAuthorization());

        return;

        static void MapRoutes(RouteGroupBuilder api)
        {
            // Jobs
            api.MapGet("/jobs", ListJobsAsync)
                .Produces<ListResearchJobsResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapGet("/jobs/{jobId:guid}", GetJobAsync)
                .Produces<GetResearchJobResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/jobs", CreateJobAsync)
                .Accepts<CreateResearchJobRequest>("application/json")
                .Produces<CreateResearchJobResponse>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/jobs/{jobId:guid}/cancel", CancelJobAsync)
                .Accepts<CancelJobRequest>("application/json")
                .Produces<CancelJobResponse>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapDelete("/jobs/{jobId:guid}", SoftDeleteJobAsync)
                .Accepts<DeleteJobRequest>("application/json")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapGet("/jobs/{jobId:guid}/sources", ListSourcesAsync)
                .Produces<ListSourcesResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapGet("/jobs/{jobId:guid}/events", ListEventsAsync)
                .Produces<IReadOnlyList<ResearchEventDto>>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/jobs/{jobId:guid}/events/stream-token", CreateEventsStreamTokenAsync)
                .Produces<CreateSseTokenResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            // Stream endpoint (anonymous but ticket-gated)
            api.MapGet("/jobs/{jobId:guid}/events/stream", StreamEventsAsync)
                .AllowAnonymous()
                .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status404NotFound);

            // Learnings
            api.MapGet("/jobs/{jobId:guid}/learnings", ListLearningsAsync)
                .Produces<ListLearningsResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/jobs/{jobId:guid}/learnings", AddLearningAsync)
                .Accepts<AddLearningRequest>("application/json")
                .Produces<AddLearningResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapDelete("/jobs/{jobId:guid}/learnings/{learningId:guid}", SoftDeleteLearningAsync)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapGet("/learnings/{learningId:guid}/group", GetLearningGroupByLearningIdAsync)
                .Produces(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/learnings/groups/resolve", ResolveLearningGroupsBatchAsync)
                .Accepts<BatchResolveLearningGroupsRequest>("application/json")
                .Produces<BatchResolveLearningGroupsResponse>(StatusCodes.Status200OK)
                .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            // Syntheses
            api.MapGet("/jobs/{jobId:guid}/syntheses", ListSynthesesAsync)
                .Produces<ListSynthesesResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapGet("/jobs/{jobId:guid}/syntheses/latest", GetLatestSynthesisAsync)
                .Produces<LatestSynthesisResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapGet("/syntheses/{synthesisId:guid}", GetSynthesisAsync)
                .Produces<SynthesisDto>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPost("/jobs/{jobId:guid}/syntheses", CreateSynthesisAsync)
                .Accepts<StartSynthesisRequest>("application/json")
                .Produces<CreateSynthesisResponse>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            // This endpoint can return 200 (terminal message) OR 202 (queued)
            api.MapPost("/syntheses/{synthesisId:guid}/run", RunSynthesisAsync)
                .Produces<RunSynthesisTerminalResponse>(StatusCodes.Status200OK)
                .Produces<RunSynthesisAcceptedResponse>(StatusCodes.Status202Accepted)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPut("/syntheses/{synthesisId:guid}/overrides/sources", UpsertSynthesisSourceOverridesAsync)
                .Accepts<IReadOnlyList<SynthesisSourceOverrideDto>>("application/json")
                .Produces<UpsertOverridesResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapPut("/syntheses/{synthesisId:guid}/overrides/learnings", UpsertSynthesisLearningOverridesAsync)
                .Accepts<IReadOnlyList<SynthesisLearningOverrideDto>>("application/json")
                .Produces<UpsertOverridesResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);

            api.MapDelete("/jobs/{jobId:guid}/sources/{sourceId:guid}", SoftDeleteSourceAsync)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }

    // ---------------- jobs ----------------

    /// <summary>
    /// POST /api/research/jobs
    /// Creates a research job and enqueues the initial deep-research run.
    /// </summary>
    /// <param name="request">Job creation parameters (query, clarifications, optional protocol parameters).</param>
    /// <param name="orchestrator">Research orchestrator that enqueues long-running work.</param>
    /// <param name="protocolService">Service to auto-select breadth/depth and language/region when missing.</param>
    /// <param name="jobStore">Job store used by orchestrator/services.</param>
    /// <param name="ct">Request cancellation token.</param>
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
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListJobsAsync(
        IResearchJobRepository jobRepository,
        CancellationToken ct)
    {
        var jobs = await jobRepository.ListJobsAsync(ct);

        var items = jobs.Select(j => new ResearchJobListItemDto(
            Id: j.Id,
            Query: j.Query,
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
    /// <param name="jobId">Research job id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
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
                    latest.CreatedAt,
                    latest.CompletedAt)
            ));
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/cancel
    /// Requests job cancellation (best-effort) and deletes queued Hangfire job if not yet running.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Optional cancellation reason.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="backgroundJobs">Hangfire client.</param>
    /// <param name="ct">Request cancellation token.</param>
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
                ResearchEventStage.Planning,
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
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Optional deletion reason.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="backgroundJobs">Hangfire client.</param>
    /// <param name="ct">Request cancellation token.</param>
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

    /// <summary>
    /// GET /api/research/jobs/{jobId}/sources
    /// Lists sources for a job.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListSourcesAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchSourceRepository sourceRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var sources = await sourceRepository.ListSourcesAsync(jobId, ct);

        var items = sources.Select(s => new SourceListItemDto(
            s.SourceId,
            s.Reference,
            s.Title,
            s.Language,
            s.Region,
            s.CreatedAt,
            s.LearningCount
        )).ToList();

        return Results.Ok(new ListSourcesResponse(jobId, items.Count, items));
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/events
    /// Lists persisted events for a job.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListEventsAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var events = await eventRepository.GetEventsAsync(jobId, ct);

        return Results.Ok(events.Select(e =>
            new ResearchEventDto(e.Id, e.Timestamp, e.Stage.ToString(), e.Message)
        ).ToList());
    }

    // ---------------- learnings ----------------

    /// <summary>
    /// GET /api/research/jobs/{jobId}/learnings
    /// Lists learnings for a job (paged).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="req">Pagination parameters.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListLearningsAsync(
        Guid jobId,
        [AsParameters] ListLearningsRequest req,
        IResearchJobRepository jobRepository,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var result = await learningRepository.ListLearningsAsync(
            jobId,
            req.SkipValue,
            req.TakeValue,
            ct);

        var items = result.Items.Select(l => new LearningListItemDto(
            l.LearningId,
            l.SourceId,
            l.SourceReference,
            l.ImportanceScore,
            l.CreatedAt,
            l.Text
        )).ToList();

        return Results.Ok(new ListLearningsResponse(
            jobId,
            req.SkipValue,
            req.TakeValue,
            result.Total,
            result.Page,
            items));
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/learnings
    /// Adds a user learning (creates/uses user source, computes embedding, assigns a group).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Learning payload.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="learningIntelService">Learning intel service.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> AddLearningAsync(
        Guid jobId,
        [FromBody] AddLearningRequest request,
        IResearchJobRepository jobRepository,
        ILearningIntelService learningIntelService,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new ErrorResponse("Text is required."));

        var score = Math.Clamp(request.ImportanceScore ?? 1.0f, 0f, 1f);

        var learning = await learningIntelService.AddUserLearningAsync(
            jobId,
            request.Text,
            score,
            request.Reference,
            request.EvidenceText,
            request.Language,
            request.Region,
            ct);

        var dto = new AddedLearningDto(
            learning.Id,
            learning.SourceId,
            learning.LearningGroupId,
            learning.ImportanceScore,
            learning.CreatedAt,
            learning.Text);

        return Results.Created(
            $"/api/learnings/{learning.Id}",
            new AddLearningResponse(jobId, dto));
    }

    /// <summary>
    /// DELETE /api/research/jobs/{jobId}/learnings/{learningId}
    /// Soft-deletes a learning.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="learningId">Learning id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> SoftDeleteLearningAsync(
        Guid jobId,
        Guid learningId,
        IResearchJobRepository jobRepository,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var ok = await learningRepository.SoftDeleteLearningAsync(jobId, learningId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// GET /api/research/learnings/{learningId}/group
    /// Returns a “group card” for the learning’s group.
    /// </summary>
    /// <param name="learningId">Learning id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> GetLearningGroupByLearningIdAsync(
        Guid learningId,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        var card = await learningRepository.GetLearningGroupCardByLearningIdAsync(learningId, ct);
        return card is null ? Results.NotFound() : Results.Ok(card);
    }

    /// <summary>
    /// POST /api/research/learnings/groups/resolve
    /// Resolves groups for multiple learning IDs in one request.
    /// </summary>
    /// <param name="request">Batch request.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ResolveLearningGroupsBatchAsync(
        [FromBody] BatchResolveLearningGroupsRequest request,
        IResearchLearningRepository learningRepository,
        CancellationToken ct)
    {
        if (request.LearningIds.Count == 0)
            return Results.BadRequest(new ErrorResponse("learningIds is required."));

        var items = await learningRepository.ResolveLearningGroupsBatchAsync(request.LearningIds, ct);
        return Results.Ok(new BatchResolveLearningGroupsResponse(items));
    }

    // ---------------- syntheses ----------------

    /// <summary>
    /// POST /api/research/jobs/{jobId}/syntheses
    /// Creates a synthesis row (no long-running work).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Synthesis creation parameters.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="synthesisService">Synthesis service.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> CreateSynthesisAsync(
        Guid jobId,
        [FromBody] StartSynthesisRequest request,
        IResearchJobRepository jobRepository,
        IResearchSynthesisRepository synthesisRepository,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        Guid? parentId = request.ParentSynthesisId;

        if (parentId is null && request.UseLatestAsParent == true)
        {
            var latest = await synthesisRepository.GetLatestSynthesisAsync(jobId, ct);
            parentId = latest?.Id;
        }

        var synthesisId = await synthesisService.CreateSynthesisAsync(
            jobId,
            parentId,
            request.Outline,
            request.Instructions,
            ct);

        return Results.Created(
            $"/api/syntheses/{synthesisId}",
            new CreateSynthesisResponse(jobId, synthesisId));
    }

    /// <summary>
    /// POST /api/research/syntheses/{synthesisId}/run
    /// Enqueues an existing synthesis run via Hangfire.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="synthesisService">Synthesis service.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> RunSynthesisAsync(
        Guid synthesisId,
        IResearchSynthesisRepository synthesisRepository,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        if (syn.Status is SynthesisStatus.Completed or SynthesisStatus.Failed)
        {
            return Results.Ok(new RunSynthesisTerminalResponse(
                syn.JobId,
                syn.Id,
                syn.Status.ToString(),
                syn.CreatedAt,
                syn.CompletedAt,
                "Synthesis is already in a terminal state.")
            );
        }

        var hangfireJobId = synthesisService.EnqueueSynthesisRun(syn.Id);

        return Results.Accepted(
            $"/api/syntheses/{syn.Id}",
            new RunSynthesisAcceptedResponse(
                syn.JobId,
                syn.Id,
                hangfireJobId,
                syn.Status.ToString(),
                syn.CreatedAt)
            );
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/syntheses
    /// Lists syntheses for a job (paged).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="req">Pagination parameters.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListSynthesesAsync(
        Guid jobId,
        [AsParameters] ListSynthesesRequest req,
        IResearchJobRepository jobRepository,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var items = await synthesisRepository.ListSynthesesAsync(
            jobId,
            req.SkipValue,
            req.TakeValue,
            ct);

        var list = items.Select(s => new SynthesisListItemDto(
            s.SynthesisId,
            s.JobId,
            s.ParentSynthesisId,
            s.Status,
            s.CreatedAt,
            s.CompletedAt,
            s.ErrorMessage,
            s.SectionCount
        )).ToList();

        return Results.Ok(new ListSynthesesResponse(
            jobId,
            req.SkipValue,
            req.TakeValue,
            items.Count,
            list));
    }


    /// <summary>
    /// GET /api/research/jobs/{jobId}/syntheses/latest
    /// Returns the latest synthesis for a job including sections.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> GetLatestSynthesisAsync(
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var syn = await synthesisRepository.GetLatestSynthesisAsync(jobId, ct);
        if (syn is null)
            return Results.Ok(new LatestSynthesisResponse(jobId, null));

        var sections = (syn.Sections ?? Array.Empty<SynthesisSection>())
            .OrderBy(s => s.Index)
            .Select(s => new SynthesisSectionDto(
                s.Id,
                s.SynthesisId,
                s.SectionKey,
                s.Index,
                s.Title,
                s.Description,
                s.IsConclusion,
                s.Summary,
                s.ContentMarkdown,
                s.CreatedAt
            ))
            .ToList();

        return Results.Ok(new LatestSynthesisResponse(
            jobId,
            new SynthesisDto(
                syn.Id,
                syn.JobId,
                syn.ParentSynthesisId,
                syn.Status.ToString(),
                syn.Outline,
                syn.Instructions,
                syn.CreatedAt,
                syn.CompletedAt,
                syn.ErrorMessage,
                sections
            )));
    }

    /// <summary>
    /// GET /api/research/syntheses/{synthesisId}
    /// Returns a synthesis by id including sections.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> GetSynthesisAsync(
        Guid synthesisId,
        IResearchSynthesisRepository synthesisRepository,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var sections = (syn.Sections ?? Array.Empty<SynthesisSection>())
            .OrderBy(s => s.Index)
            .Select(s => new SynthesisSectionDto(
                s.Id,
                s.SynthesisId,
                s.SectionKey,
                s.Index,
                s.Title,
                s.Description,
                s.IsConclusion,
                s.Summary,
                s.ContentMarkdown,
                s.CreatedAt
            ))
            .ToList();

        return Results.Ok(new SynthesisDto(
            syn.Id,
            syn.JobId,
            syn.ParentSynthesisId,
            syn.Status.ToString(),
            syn.Outline,
            syn.Instructions,
            syn.CreatedAt,
            syn.CompletedAt,
            syn.ErrorMessage,
            sections));
    }

    /// <summary>
    /// PUT /api/research/syntheses/{synthesisId}/overrides/sources
    /// Upserts source-level overrides for a synthesis.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="overrides">Overrides.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> UpsertSynthesisSourceOverridesAsync(
        Guid synthesisId,
        [FromBody] IReadOnlyList<SynthesisSourceOverrideDto> overrides,
        IResearchSynthesisRepository synthesisRepository,
        IResearchSynthesisOverridesRepository synthesisOverridesRepository,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var list = overrides?.ToList() ?? new();
        if (list.Count == 0)
            return Results.Ok(new UpsertOverridesResponse(synthesisId, 0));

        await synthesisOverridesRepository.AddOrUpdateSynthesisSourceOverridesAsync(synthesisId, list, ct);
        return Results.Ok(new UpsertOverridesResponse(synthesisId, list.Count));
    }


    /// <summary>
    /// PUT /api/research/syntheses/{synthesisId}/overrides/learnings
    /// Upserts learning-level overrides for a synthesis.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="overrides">Overrides.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> UpsertSynthesisLearningOverridesAsync(
        Guid synthesisId,
        [FromBody] IReadOnlyList<SynthesisLearningOverrideDto> overrides,
        IResearchSynthesisRepository synthesisRepository,
        IResearchSynthesisOverridesRepository synthesisOverridesRepository,
        CancellationToken ct)
    {
        var syn = await synthesisRepository.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var list = overrides?.ToList() ?? new();
        if (list.Count == 0)
            return Results.Ok(new UpsertOverridesResponse(synthesisId, 0));

        await synthesisOverridesRepository.AddOrUpdateSynthesisLearningOverridesAsync(synthesisId, list, ct);
        return Results.Ok(new UpsertOverridesResponse(synthesisId, list.Count));
    }

    /// <summary>
    /// DELETE /api/research/jobs/{jobId}/sources/{sourceId}
    /// Soft-deletes a source.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="sourceId">Source id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> SoftDeleteSourceAsync(
        Guid jobId,
        Guid sourceId,
        IResearchJobRepository jobRepository,
        IResearchSourceRepository sourceRepository,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var ok = await sourceRepository.SoftDeleteSourceAsync(jobId, sourceId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/events/stream-token
    /// Mints a short-lived ticket for opening the SSE stream via EventSource (no custom headers).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="httpContext">HTTP context (used to build stream URL).</param>
    /// <param name="user">Authenticated user.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="tickets">Ticket service.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> CreateEventsStreamTokenAsync(
        Guid jobId,
        HttpContext httpContext,
        ClaimsPrincipal user,
        IResearchJobRepository jobRepository,
        IJobSseTicketService tickets,
        CancellationToken ct)
    {
        // Ensure job exists (and optionally enforce ownership/authorization)
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        // Optional: enforce job ownership here if your store supports it.
        // Example patterns (choose one that matches your codebase):
        // await jobStore.AssertUserCanAccessJobAsync(jobId, user, ct);
        // var can = await jobStore.CanAccessJobAsync(jobId, user, ct); if (!can) return Results.Forbid();

        var ticket = tickets.Create(jobId, user);

        var tokenPath = httpContext.Request.Path.Value ?? $"/api/jobs/{jobId}/events/stream-token";
        var streamPath = tokenPath.EndsWith("/events/stream-token", StringComparison.OrdinalIgnoreCase)
            ? tokenPath[..^("/events/stream-token".Length)] + "/events/stream"
            : $"/api/jobs/{jobId}/events/stream";
        var streamUrl = $"{streamPath}?ticket={Uri.EscapeDataString(ticket)}";

        var expiresAtUtc = tickets.GetExpiryUtc(ticket);

        return Results.Ok(new CreateSseTokenResponse(
            JobId: jobId,
            Ticket: ticket,
            StreamUrl: streamUrl,
            ExpiresAtUtc: expiresAtUtc
        ));
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/events/stream
    /// Server-sent events stream of job events (replay + live). Anonymous, but requires a valid ticket.
    /// </summary>
    /// <param name="httpContext">HTTP context used for writing SSE.</param>
    /// <param name="jobId">Research job id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="eventBus">Event bus for live events.</param>
    /// <param name="tickets">Ticket validator.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task StreamEventsAsync(
        HttpContext httpContext,
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        IResearchEventBus eventBus,
        IJobSseTicketService tickets,
        CancellationToken ct)
    {

        // Ticket gate (EventSource cannot send Authorization header)
        var ticket = httpContext.Request.Query["ticket"].ToString();
        if (string.IsNullOrWhiteSpace(ticket) || !tickets.TryValidate(jobId, ticket, out _))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ConfigureSseHeaders(httpContext);

        var jsonOptions = CreateJsonOptions();
        var lastId = GetLastEventIdAsInt(httpContext);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, httpContext.RequestAborted);
        var token = linkedCts.Token;

        var lastSentId = lastId;

        static bool IsTerminal(ResearchEventStage stage)
            => stage is ResearchEventStage.Completed or ResearchEventStage.Failed or ResearchEventStage.Canceled;

        async Task<bool> TryWriteEventAsync(ResearchEvent ev, CancellationToken t)
        {
            if (ev.Id <= Volatile.Read(ref lastSentId))
                return false;

            await WriteEventAsync(httpContext, jsonOptions, ev, t);
            Volatile.Write(ref lastSentId, ev.Id);
            return true;
        }

        async Task WriteDoneNowAsync(ResearchEvent terminalEvent, CancellationToken t)
        {
            var doneId = Volatile.Read(ref lastSentId) + 1;

            var done = new ResearchDoneSseDto(
                JobId: jobId,
                Status: terminalEvent.Stage.ToString(),
                SynthesisId: terminalEvent.SynthesisId
            );

            await WriteSseAsync(
                httpContext,
                jsonOptions,
                eventName: "done",
                id: doneId.ToString(),
                data: done,
                token: t);

            linkedCts.Cancel();
        }

        // Subscribe FIRST
        await using var subscription = await eventBus.SubscribeAsync(
            jobId,
            async (ev, t) =>
            {
                if (t.IsCancellationRequested)
                    return;

                await TryWriteEventAsync(ev, t);

                if (IsTerminal(ev.Stage))
                    await WriteDoneNowAsync(ev, t);
            },
            token);

        // Replay stored events AFTER subscribing
        var storedEvents = await eventRepository.GetEventsAsync(jobId, token);

        foreach (var e in storedEvents.Where(e => e.Id > lastId).OrderBy(e => e.Id))
        {
            if (token.IsCancellationRequested)
                break;

            await TryWriteEventAsync(e, token);

            if (IsTerminal(e.Stage))
            {
                await WriteDoneNowAsync(e, token);
                return;
            }
        }

        try
        {
            while (!token.IsCancellationRequested)
                await Task.Delay(500, token);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }


    // ---------------- helpers ---------------- (unchanged except SSE payloads)

    private static void ConfigureSseHeaders(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers["Content-Type"] = "text/event-stream";
        httpContext.Response.Headers["Cache-Control"] = "no-cache";
        httpContext.Response.Headers["Connection"] = "keep-alive";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
    }

    private static JsonSerializerOptions CreateJsonOptions()
        => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static int GetLastEventIdAsInt(HttpContext httpContext)
    {
        var raw = httpContext.Request.Headers["Last-Event-ID"].ToString();
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private static async Task WriteSseAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        string eventName,
        string id,
        object data,
        CancellationToken token)
    {
        var sb = new StringBuilder();
        sb.Append("id: ").Append(id).Append('\n');
        sb.Append("event: ").Append(eventName).Append('\n');
        sb.Append("data: ").Append(JsonSerializer.Serialize(data, jsonOptions)).Append("\n\n");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await httpContext.Response.Body.WriteAsync(bytes, token);
        await httpContext.Response.Body.FlushAsync(token);
    }

    private static Task WriteEventAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        ResearchEvent e,
        CancellationToken token)
        => WriteSseAsync(
            httpContext,
            jsonOptions,
            eventName: "event",
            id: e.Id.ToString(),
            data: new ResearchEventSseDto(
                Id: e.Id,
                Timestamp: e.Timestamp,
                Stage: e.Stage.ToString(),
                Message: e.Message
            ),
            token: token);
}
