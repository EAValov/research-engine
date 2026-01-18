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
        var api = app.MapGroup("/api/research")
            .WithTags("Research Jobs API")
            .RequireAuthorization();

        // =========================
        // Jobs
        // =========================
        api.MapGet   ("/jobs",                ListJobsAsync);
        api.MapGet   ("/jobs/{jobId:guid}",   GetJobAsync);
        api.MapPost  ("/jobs",                CreateJobAsync);
        api.MapPost  ("/jobs/{jobId:guid}/cancel", CancelJobAsync);
        api.MapDelete("/jobs/{jobId:guid}",   SoftDeleteJobAsync);

        api.MapGet   ("/jobs/{jobId:guid}/sources", ListSourcesAsync);

        api.MapGet   ("/jobs/{jobId:guid}/events",        ListEventsAsync);
        api.MapGet   ("/jobs/{jobId:guid}/events/stream", StreamEventsAsync);

        // =========================
        // Learnings
        // =========================
        api.MapGet   ("/jobs/{jobId:guid}/learnings", ListLearningsAsync);
        api.MapPost  ("/jobs/{jobId:guid}/learnings", AddLearningAsync);
        api.MapDelete("/jobs/{jobId:guid}/learnings/{learningId:guid}", SoftDeleteLearningAsync);

        api.MapGet   ("/learnings/{learningId:guid}/group", GetLearningGroupByLearningIdAsync);
        api.MapPost  ("/learnings/groups/resolve", ResolveLearningGroupsBatchAsync);

        // =========================
        // Syntheses
        // =========================
        api.MapGet ("/jobs/{jobId:guid}/syntheses",        ListSynthesesAsync);
        api.MapGet ("/jobs/{jobId:guid}/syntheses/latest", GetLatestSynthesisAsync);
        api.MapGet ("/syntheses/{synthesisId:guid}",       GetSynthesisAsync);

        api.MapPost("/jobs/{jobId:guid}/syntheses",        CreateSynthesisAsync);
        api.MapPost("/syntheses/{synthesisId:guid}/run",   RunSynthesisAsync);

        api.MapPut ("/syntheses/{synthesisId:guid}/overrides/sources",   UpsertSynthesisSourceOverridesAsync);
        api.MapPut ("/syntheses/{synthesisId:guid}/overrides/learnings", UpsertSynthesisLearningOverridesAsync);

        // =========================
        // Sources
        // =========================
        api.MapDelete("/jobs/{jobId:guid}/sources/{sourceId:guid}", SoftDeleteSourceAsync);
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        // Compute missing protocol params
        int? breadth = request.Breadth;
        int? depth = request.Depth;
        string? language = request.Language;
        string? region = request.Region;

        var clarifications = request.Clarifications?.Select(c => new Clarification
        {
            Question = c.Question,
            Answer = c.Answer
        }).ToList() ?? new List<Clarification>();

        if (!breadth.HasValue || !depth.HasValue)
            (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(request.Query, clarifications, ct);

        if (string.IsNullOrEmpty(language))
            (language, region) = await protocolService.AutoSelectLanguageRegionAsync(request.Query, clarifications, ct);

        // Create job row + start run
        var jobId = await orchestrator.StartJobAsync(
            request.Query,
            clarifications,
            breadth ?? 2,
            depth ?? 2,
            language ?? "en",
            region,
            ct);

        return Results.Ok(new
        {
            jobId,
            links = new
            {
                self = $"/api/research/jobs/{jobId}",
                listJobs = "/api/research/jobs",
                sources = $"/api/research/jobs/{jobId}/sources",
                learnings = $"/api/research/jobs/{jobId}/learnings",
                addLearning = $"/api/research/jobs/{jobId}/learnings",
                createSynthesis = $"/api/research/jobs/{jobId}/syntheses",
                listSyntheses = $"/api/research/jobs/{jobId}/syntheses",
                latestSynthesis = $"/api/research/jobs/{jobId}/syntheses/latest",
                events = $"/api/research/jobs/{jobId}/events",
                stream = $"/api/research/jobs/{jobId}/events/stream",
                cancel = $"/api/research/jobs/{jobId}/cancel",
                delete = $"/api/research/jobs/{jobId}"
            }
        });
    }

    /// <summary>
    /// GET /api/research/jobs
    /// Lists jobs for the UX sidebar.
    /// </summary>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListJobsAsync(
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var jobs = await jobStore.ListJobsAsync(ct);

        return Results.Ok(new
        {
            count = jobs.Count,
            jobs = jobs.Select(j => new
            {
                id = j.Id,
                query = j.Query,
                breadth = j.Breadth,
                depth = j.Depth,
                status = j.Status.ToString(),
                targetLanguage = j.TargetLanguage,
                region = j.Region,
                createdAt = j.CreatedAt,
                updatedAt = j.UpdatedAt,
                links = new
                {
                    self = $"/api/research/jobs/{j.Id}",
                    sources = $"/api/research/jobs/{j.Id}/sources",
                    learnings = $"/api/research/jobs/{j.Id}/learnings",
                    syntheses = $"/api/research/jobs/{j.Id}/syntheses",
                    latestSynthesis = $"/api/research/jobs/{j.Id}/syntheses/latest",
                    events = $"/api/research/jobs/{j.Id}/events",
                    stream = $"/api/research/jobs/{j.Id}/events/stream"
                }
            })
        });
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var latestSynthesis = job.Syntheses?
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

        return Results.Ok(new
        {
            id = job.Id,
            query = job.Query,
            breadth = job.Breadth,
            depth = job.Depth,
            status = job.Status.ToString(),
            targetLanguage = job.TargetLanguage,
            region = job.Region,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt,
            clarifications = job.Clarifications.Select(c => new { c.Question, c.Answer }),
            sourcesCount = job.Sources.Count,
            synthesesCount = job.Syntheses!.Count,
            latestSynthesis = latestSynthesis is null ? null : new
            {
                id = latestSynthesis.Id,
                status = latestSynthesis.Status.ToString(),
                createdAt = latestSynthesis.CreatedAt,
                completedAt = latestSynthesis.CompletedAt
            },
            links = new
            {
                self = $"/api/research/jobs/{job.Id}",
                listJobs = "/api/research/jobs",
                sources = $"/api/research/jobs/{job.Id}/sources",
                learnings = $"/api/research/jobs/{job.Id}/learnings",
                syntheses = $"/api/research/jobs/{job.Id}/syntheses",
                latestSynthesis = $"/api/research/jobs/{job.Id}/syntheses/latest",
                events = $"/api/research/jobs/{job.Id}/events",
                stream = $"/api/research/jobs/{job.Id}/events/stream",
                cancel = $"/api/research/jobs/{job.Id}/cancel",
                delete = $"/api/research/jobs/{job.Id}"
            }
        });
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/cancel
    /// Requests job cancellation (best-effort) and deletes queued Hangfire job if not yet running.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Optional cancellation reason.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="backgroundJobs">Hangfire client used to delete queued background job.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> CancelJobAsync(
        Guid jobId,
        [FromBody] CancelJobRequest? request,
        IResearchJobStore jobStore,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null) return Results.NotFound();

        await jobStore.RequestJobCancelAsync(jobId, request?.Reason, ct);

        await jobStore.AppendEventAsync(
            jobId,
            new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Planning,
                $"Cancel requested{(string.IsNullOrWhiteSpace(request?.Reason) ? "" : $": {request!.Reason}")}"),
            ct);

        // prevents start if still queued
        if (!string.IsNullOrWhiteSpace(job.HangfireJobId))
            backgroundJobs.Delete(job.HangfireJobId);

        return Results.Accepted(null, new { jobId, status = "cancel_requested" });
    }

    /// <summary>
    /// DELETE /api/research/jobs/{jobId}
    /// Soft-deletes a job (and prevents queued Hangfire job from starting).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Optional deletion reason.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="backgroundJobs">Hangfire client used to delete queued background job.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> SoftDeleteJobAsync(
        Guid jobId,
        [FromBody] DeleteJobRequest? request,
        IResearchJobStore jobStore,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null) return Results.NotFound();

        // prevent queued job from starting
        if (!string.IsNullOrWhiteSpace(job.HangfireJobId))
            backgroundJobs.Delete(job.HangfireJobId);

        await jobStore.SoftDeleteJobAsync(jobId, request?.Reason, ct);

        await jobStore.AppendEventAsync(
            jobId,
            new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Planning,
                $"Job deleted (soft){(string.IsNullOrWhiteSpace(request?.Reason) ? "" : $": {request!.Reason}")}"),
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var sources = await jobStore.ListSourcesAsync(jobId, ct);

        return Results.Ok(new
        {
            jobId,
            count = sources.Count,
            sources = sources.Select(s => new
            {
                sourceId = s.SourceId,
                reference = s.Reference,
                title = s.Title,
                language = s.Language,
                region = s.Region,
                createdAt = s.CreatedAt,
                learningCount = s.LearningCount,
                links = new
                {
                    delete = $"/api/research/jobs/{jobId}/sources/{s.SourceId}"
                }
            })
        });
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var events = await jobStore.GetEventsAsync(jobId, ct);

        var response = events.Select(e => new
        {
            id = e.Id,
            timestamp = e.Timestamp,
            stage = e.Stage.ToString(),
            message = e.Message
        });

        return Results.Ok(response);
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/events/stream
    /// Server-sent events stream of job events (replay + live).
    /// </summary>
    /// <param name="httpContext">HTTP context used for writing SSE.</param>
    /// <param name="jobId">Research job id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="eventBus">Event bus for live events.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task StreamEventsAsync(
        HttpContext httpContext,
        Guid jobId,
        IResearchJobStore jobStore,
        IResearchEventBus eventBus,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
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

            await WriteSseAsync(
                httpContext,
                jsonOptions,
                eventName: "done",
                id: doneId.ToString(),
                data: new
                {
                    jobId,
                    status = terminalEvent.Stage.ToString(),
                    synthesisId = terminalEvent.SynthesisId
                },
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
        var storedEvents = await jobStore.GetEventsAsync(jobId, token);

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

    // ---------------- learnings ----------------

    /// <summary>
    /// GET /api/research/jobs/{jobId}/learnings
    /// Lists learnings for a job (paged).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="req">Pagination parameters (skip/take).</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListLearningsAsync(
        Guid jobId,
        [AsParameters] ListLearningsRequest req,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var s = req.SkipValue;
        var t = req.TakeValue;

        var learnings = await jobStore.ListLearningsAsync(jobId, s, t, ct);

        return Results.Ok(new
        {
            jobId,
            skip = s,
            take = t,
            total = learnings.Total,
            page = learnings.Page,
            learnings = learnings.Items.Select(l => new
            {
                learningId = l.LearningId,
                sourceId = l.SourceId,
                sourceReference = l.SourceReference,
                importanceScore = l.ImportanceScore,
                createdAt = l.CreatedAt,
                text = l.Text,
                links = new
                {
                    group = $"/api/research/learnings/{l.LearningId}/group",
                    delete = $"/api/research/jobs/{jobId}/learnings/{l.LearningId}"
                }
            })
        });
    }

    /// <summary>
    /// POST /api/research/jobs/{jobId}/learnings
    /// Adds a user learning (creates/uses user source, computes embedding, assigns a group).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Learning payload (text + optional reference, evidence, language, region, score).</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="learningIntelService">Learning intel service that persists embeddings + grouping.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> AddLearningAsync(
        Guid jobId,
        [FromBody] AddLearningRequest request,
        IResearchJobStore jobStore,
        ILearningIntelService learningIntelService,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        if (request is null || string.IsNullOrWhiteSpace(request.Text))
            return Results.BadRequest(new { error = "Text is required." });

        var score = request.ImportanceScore ?? 1.0f;
        if (float.IsNaN(score) || score <= 0) score = 1.0f;
        score = Math.Clamp(score, 0.0f, 1.0f);

        var learning = await learningIntelService.AddUserLearningAsync(
            jobId: jobId,
            text: request.Text,
            importanceScore: score,
            reference: request.Reference,
            evidenceText: request.EvidenceText,
            language: request.Language,
            region: request.Region,
            ct: ct);

        return Results.Ok(new
        {
            jobId,
            learning = new
            {
                learningId = learning.Id,
                sourceId = learning.SourceId,
                learningGroupId = learning.LearningGroupId,
                importanceScore = learning.ImportanceScore,
                createdAt = learning.CreatedAt,
                text = learning.Text
            },
            links = new
            {
                group = $"/api/research/learnings/{learning.Id}/group",
                delete = $"/api/research/jobs/{jobId}/learnings/{learning.Id}"
            }
        });
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var ok = await jobStore.SoftDeleteLearningAsync(jobId, learningId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// GET /api/research/learnings/{learningId}/group
    /// Returns a “group card” for the learning’s group (for citation hover UI).
    /// </summary>
    /// <param name="learningId">Learning id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> GetLearningGroupByLearningIdAsync(
        Guid learningId,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var card = await jobStore.GetLearningGroupCardByLearningIdAsync(learningId, ct);
        if (card is null)
            return Results.NotFound();

        return Results.Ok(card);
    }

    /// <summary>
    /// POST /api/research/learnings/groups/resolve
    /// Resolves groups for multiple learning IDs in one request.
    /// </summary>
    /// <param name="request">Batch request with learning ids.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ResolveLearningGroupsBatchAsync(
        [FromBody] BatchResolveLearningGroupsRequest request,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        if (request is null || request.LearningIds is null || request.LearningIds.Count == 0)
            return Results.BadRequest(new { error = "learningIds is required." });

        var items = await jobStore.ResolveLearningGroupsBatchAsync(request.LearningIds, ct);
        return Results.Ok(new BatchResolveLearningGroupsResponse(items));
    }

    // ---------------- syntheses ----------------

    /// <summary>
    /// POST /api/research/jobs/{jobId}/syntheses
    /// Creates a synthesis row (no long-running work). Client can apply overrides and call /run.
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="request">Synthesis creation parameters (parent selection, outline, instructions).</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="synthesisService">Synthesis service (creates row).</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> CreateSynthesisAsync(
        Guid jobId,
        [FromBody] StartSynthesisRequest request,
        IResearchJobStore jobStore,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        Guid? parentId = request.ParentSynthesisId;

        if (parentId is null && request.UseLatestAsParent == true)
        {
            var latest = await jobStore.GetLatestSynthesisAsync(jobId, ct);
            parentId = latest?.Id;
        }

        var synthesisId = await synthesisService.CreateSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentId,
            outline: request.Outline,
            instructions: request.Instructions,
            ct: ct);

        return Results.Ok(new
        {
            jobId,
            synthesisId,
            links = new
            {
                self = $"/api/research/syntheses/{synthesisId}",
                run = $"/api/research/syntheses/{synthesisId}/run",
                latest = $"/api/research/jobs/{jobId}/syntheses/latest",
                list = $"/api/research/jobs/{jobId}/syntheses",
                overridesSources = $"/api/research/syntheses/{synthesisId}/overrides/sources",
                overridesLearnings = $"/api/research/syntheses/{synthesisId}/overrides/learnings",
                events = $"/api/research/jobs/{jobId}/events",
                stream = $"/api/research/jobs/{jobId}/events/stream"
            }
        });
    }

    /// <summary>
    /// POST /api/research/syntheses/{synthesisId}/run
    /// Enqueues an existing synthesis run via Hangfire.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="synthesisService">Synthesis service (enqueues run).</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> RunSynthesisAsync(
        Guid synthesisId,
        IResearchJobStore jobStore,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        var syn = await jobStore.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        if (syn.Status is SynthesisStatus.Completed or SynthesisStatus.Failed)
        {
            return Results.Ok(new
            {
                jobId = syn.JobId,
                synthesisId = syn.Id,
                status = syn.Status.ToString(),
                createdAt = syn.CreatedAt,
                completedAt = syn.CompletedAt,
                message = "Synthesis is already in a terminal state.",
                links = new
                {
                    self = $"/api/research/syntheses/{syn.Id}",
                    latest = $"/api/research/jobs/{syn.JobId}/syntheses/latest",
                    list = $"/api/research/jobs/{syn.JobId}/syntheses",
                    events = $"/api/research/jobs/{syn.JobId}/events",
                    stream = $"/api/research/jobs/{syn.JobId}/events/stream"
                }
            });
        }

        var hangfireJobId = synthesisService.EnqueueSynthesisRun(syn.Id);

        return Results.Accepted($"/api/research/syntheses/{syn.Id}", new
        {
            jobId = syn.JobId,
            synthesisId = syn.Id,
            hangfireJobId,
            status = syn.Status.ToString(),
            createdAt = syn.CreatedAt,
            links = new
            {
                self = $"/api/research/syntheses/{syn.Id}",
                latest = $"/api/research/jobs/{syn.JobId}/syntheses/latest",
                list = $"/api/research/jobs/{syn.JobId}/syntheses",
                overridesSources = $"/api/research/syntheses/{syn.Id}/overrides/sources",
                overridesLearnings = $"/api/research/syntheses/{syn.Id}/overrides/learnings",
                events = $"/api/research/jobs/{syn.JobId}/events",
                stream = $"/api/research/jobs/{syn.JobId}/events/stream"
            }
        });
    }

    /// <summary>
    /// GET /api/research/jobs/{jobId}/syntheses
    /// Lists syntheses for a job (paged).
    /// </summary>
    /// <param name="jobId">Research job id.</param>
    /// <param name="req">Pagination parameters (skip/take).</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> ListSynthesesAsync(
        Guid jobId,
        [AsParameters] ListSynthesesRequest req,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var skip = req.SkipValue;
        var take = req.TakeValue;

        var items = await jobStore.ListSynthesesAsync(jobId, skip, take, ct);

        return Results.Ok(new
        {
            jobId,
            skip,
            take,
            count = items.Count,
            syntheses = items.Select(s => new
            {
                synthesisId = s.SynthesisId,
                jobId = s.JobId,
                parentSynthesisId = s.ParentSynthesisId,
                status = s.Status,
                createdAt = s.CreatedAt,
                completedAt = s.CompletedAt,
                errorMessage = s.ErrorMessage,
                sectionCount = s.SectionCount,
                links = new
                {
                    self = $"/api/research/syntheses/{s.SynthesisId}",
                    run = $"/api/research/syntheses/{s.SynthesisId}/run"
                }
            })
        });
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var syn = await jobStore.GetLatestSynthesisAsync(jobId, ct);
        if (syn is null)
            return Results.Ok(new { jobId, synthesis = (object?)null });

        var sections = (syn.Sections ?? Array.Empty<SynthesisSection>())
            .OrderBy(s => s.Index)
            .Select(s => new
            {
                id = s.Id,
                synthesisId = s.SynthesisId,
                sectionKey = s.SectionKey,
                index = s.Index,
                title = s.Title,
                description = s.Description,
                isConclusion = s.IsConclusion,
                summary = s.Summary,
                contentMarkdown = s.ContentMarkdown,
                createdAt = s.CreatedAt
            })
            .ToList();

        return Results.Ok(new
        {
            jobId,
            synthesis = new
            {
                id = syn.Id,
                jobId = syn.JobId,
                parentSynthesisId = syn.ParentSynthesisId,
                status = syn.Status.ToString(),
                outline = syn.Outline,
                instructions = syn.Instructions,
                createdAt = syn.CreatedAt,
                completedAt = syn.CompletedAt,
                errorMessage = syn.ErrorMessage,
                sections,
                links = new
                {
                    self = $"/api/research/syntheses/{syn.Id}",
                    run = $"/api/research/syntheses/{syn.Id}/run",
                    overridesSources = $"/api/research/syntheses/{syn.Id}/overrides/sources",
                    overridesLearnings = $"/api/research/syntheses/{syn.Id}/overrides/learnings"
                }
            }
        });
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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var syn = await jobStore.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var sections = (syn.Sections ?? Array.Empty<SynthesisSection>())
            .OrderBy(s => s.Index)
            .Select(s => new
            {
                id = s.Id,
                synthesisId = s.SynthesisId,
                sectionKey = s.SectionKey,
                index = s.Index,
                title = s.Title,
                description = s.Description,
                isConclusion = s.IsConclusion,
                summary = s.Summary,
                contentMarkdown = s.ContentMarkdown,
                createdAt = s.CreatedAt
            })
            .ToList();

        return Results.Ok(new
        {
            id = syn.Id,
            jobId = syn.JobId,
            parentSynthesisId = syn.ParentSynthesisId,
            status = syn.Status.ToString(),
            outline = syn.Outline,
            instructions = syn.Instructions,
            createdAt = syn.CreatedAt,
            completedAt = syn.CompletedAt,
            errorMessage = syn.ErrorMessage,
            sections,
            links = new
            {
                run = $"/api/research/syntheses/{syn.Id}/run",
                overridesSources = $"/api/research/syntheses/{syn.Id}/overrides/sources",
                overridesLearnings = $"/api/research/syntheses/{syn.Id}/overrides/learnings"
            }
        });
    }

    /// <summary>
    /// PUT /api/research/syntheses/{synthesisId}/overrides/sources
    /// Upserts source-level overrides (pinned/excluded) for a synthesis.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="overrides">Overrides to upsert.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> UpsertSynthesisSourceOverridesAsync(
        Guid synthesisId,
        [FromBody] IReadOnlyList<SynthesisSourceOverrideDto> overrides,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var syn = await jobStore.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var list = overrides?.ToList() ?? new List<SynthesisSourceOverrideDto>();
        if (list.Count == 0)
            return Results.Ok(new { synthesisId, updated = 0 });

        await jobStore.AddOrUpdateSynthesisSourceOverridesAsync(synthesisId, list, ct);

        return Results.Ok(new { synthesisId, updated = list.Count });
    }

    /// <summary>
    /// PUT /api/research/syntheses/{synthesisId}/overrides/learnings
    /// Upserts learning-level overrides (pinned/excluded/scoreOverride) for a synthesis.
    /// </summary>
    /// <param name="synthesisId">Synthesis id.</param>
    /// <param name="overrides">Overrides to upsert.</param>
    /// <param name="jobStore">Job store.</param>
    /// <param name="ct">Request cancellation token.</param>
    private static async Task<IResult> UpsertSynthesisLearningOverridesAsync(
        Guid synthesisId,
        [FromBody] IReadOnlyList<SynthesisLearningOverrideDto> overrides,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var syn = await jobStore.GetSynthesisAsync(synthesisId, ct);
        if (syn is null)
            return Results.NotFound();

        var list = overrides?.ToList() ?? new List<SynthesisLearningOverrideDto>();
        if (list.Count == 0)
            return Results.Ok(new { synthesisId, updated = 0 });

        await jobStore.AddOrUpdateSynthesisLearningOverridesAsync(synthesisId, list, ct);

        return Results.Ok(new { synthesisId, updated = list.Count });
    }

    // ---------------- sources ----------------

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
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var ok = await jobStore.SoftDeleteSourceAsync(jobId, sourceId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    // ---------------- helpers ----------------

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
            data: new { id = e.Id, timestamp = e.Timestamp, stage = e.Stage, message = e.Message },
            token: token);
}