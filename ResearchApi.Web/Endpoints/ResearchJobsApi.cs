using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResearchApi.Domain;

public static class ResearchJobsApi
{
    public static void MapResearchJobsApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/research")
            .WithTags("Research Jobs API")
            .RequireAuthorization();

        api.MapPost("/jobs", CreateJobAsync);
        api.MapGet("/jobs/{jobId:guid}", GetJobAsync);

        api.MapGet("/jobs/{jobId:guid}/sources", ListSourcesAsync);
        api.MapGet("/jobs/{jobId:guid}/learnings", ListLearningsAsync);

        api.MapPost("/jobs/{jobId:guid}/syntheses", StartSynthesisAsync);
        api.MapGet("/jobs/{jobId:guid}/syntheses/latest", GetLatestSynthesisAsync);

        api.MapGet("/syntheses/{synthesisId:guid}", GetSynthesisAsync);

        api.MapGet("/jobs/{jobId:guid}/events", ListEventsAsync);
        api.MapGet("/jobs/{jobId:guid}/events/stream", StreamEventsAsync);
    }

    // ---------------- jobs ----------------

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

        // Create job row + start run in background (your new orchestrator contract)
        var jobId = await orchestrator.StartJobAsync(
            request.Query,
            clarifications,
            breadth ?? 2,
            depth ?? 2,
            language ?? "en",
            region,
            ct);

        var response = new
        {
            jobId,
            links = new
            {
                self = $"/api/research/jobs/{jobId}",
                sources = $"/api/research/jobs/{jobId}/sources",
                learnings = $"/api/research/jobs/{jobId}/learnings",
                startSynthesis = $"/api/research/jobs/{jobId}/syntheses",
                latestSynthesis = $"/api/research/jobs/{jobId}/syntheses/latest",
                events = $"/api/research/jobs/{jobId}/events",
                stream = $"/api/research/jobs/{jobId}/events/stream"
            }
        };

        return Results.Ok(response);
    }

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

        var response = new
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

            // synthesis info instead of job.ReportMarkdown
            latestSynthesis = latestSynthesis is null ? null : new
            {
                id = latestSynthesis.Id,
                status = latestSynthesis.Status.ToString(),
                createdAt = latestSynthesis.CreatedAt,
                completedAt = latestSynthesis.CompletedAt
            }
        };

        return Results.Ok(response);
    }

    // ---------------- listing for UX (overrides UI) ----------------

    private static async Task<IResult> ListSourcesAsync(
        Guid jobId,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var sources = await jobStore.ListSourcesAsync(jobId, ct);

        var response = new
        {
            jobId,
            count = sources.Count,
            sources = sources.Select(s => new
            {
                sourceId = s.SourceId,
                url = s.Url,
                title = s.Title,
                language = s.Language,
                region = s.Region,
                createdAt = s.CreatedAt,
                learningCount = s.LearningCount
            })
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> ListLearningsAsync(
        Guid jobId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        var s = Math.Max(skip ?? 0, 0);
        var t = Math.Clamp(take ?? 200, 1, 500);

        var learnings = await jobStore.ListLearningsAsync(jobId, s, t, ct);

        var response = new
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
                sourceUrl = l.SourceUrl,
                importanceScore = l.ImportanceScore,
                createdAt = l.CreatedAt,
                text = l.Text
            })
        };

        return Results.Ok(response);
    }

    // ---------------- synthesis ----------------

    private static async Task<IResult> StartSynthesisAsync(
        Guid jobId,
        [FromBody] StartSynthesisRequest request,
        IResearchJobStore jobStore,
        IReportSynthesisService synthesisService,
        CancellationToken ct)
    {
        // Validate job exists
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        // Determine parent synthesis
        Guid? parentId = request.ParentSynthesisId;

        if (parentId is null && request.UseLatestAsParent == true)
        {
            var latest = await jobStore.GetLatestSynthesisAsync(jobId, ct);
            parentId = latest?.Id;
        }

        // Create synthesis row NOW (so client can watch events / status)
        var synthesis = await jobStore.CreateSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentId,
            outline: request.Outline,
            instructions: request.Instructions,
            ct: ct);

        // Persist overrides (optional)
        if (request.SourceOverrides is { Count: > 0 })
        {
            await jobStore.AddOrUpdateSynthesisSourceOverridesAsync(
                synthesis.Id, request.SourceOverrides, ct);
        }

        if (request.LearningOverrides is { Count: > 0 })
        {
            await jobStore.AddOrUpdateSynthesisLearningOverridesAsync(
                synthesis.Id, request.LearningOverrides, ct);
        }

        // Fire-and-forget synthesis run
        _ = Task.Run(async () =>
        {
            try
            {
                await synthesisService.RunExistingSynthesisAsync(
                    synthesisId: synthesis.Id,
                    progress: null, // start from scratch
                    ct: CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[deep-research-synthesis] RunSynthesisAsync failed: {ex}");
            }
        });

        var response = new
        {
            jobId,
            synthesisId = synthesis.Id,
            status = synthesis.Status.ToString(),
            createdAt = synthesis.CreatedAt,
            links = new
            {
                self = $"/api/research/syntheses/{synthesis.Id}",
                latest = $"/api/research/jobs/{jobId}/syntheses/latest",
                events = $"/api/research/jobs/{jobId}/events",
                stream = $"/api/research/jobs/{jobId}/events/stream"
            }
        };

        return Results.Ok(response);
    }

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

        // Defensive: keep stable order
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
                sections
            }
        });
    }

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
            sections
        });
    }

    // ---------------- events ----------------

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
            stage = e.Stage,
            message = e.Message
        });

        return Results.Ok(response);
    }

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

        // highest event id we've actually written to the client
        var lastSentId = lastId;

        static bool IsTerminal(ResearchEventStage stage)
            => stage is ResearchEventStage.Completed or ResearchEventStage.Failed;

        async Task<bool> TryWriteEventAsync(ResearchEvent ev, CancellationToken t)
        {
            // Dedupe between replay and live
            if (ev.Id <= Volatile.Read(ref lastSentId))
                return false;

            await WriteEventAsync(httpContext, jsonOptions, ev, t);
            Volatile.Write(ref lastSentId, ev.Id);
            return true;
        }

        async Task WriteDoneNowAsync(ResearchEvent terminalEvent, CancellationToken t)
        {
            // Use numeric id for maximum SSE client compatibility
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
                    // Requires ResearchEvent.SynthesisId? to exist (nullable)
                    synthesisId = terminalEvent.SynthesisId
                },
                token: t);

            // After "done", we end the stream deterministically.
            linkedCts.Cancel();
        }

        // 1) Subscribe FIRST to close race window
        await using var subscription = await eventBus.SubscribeAsync(
            jobId,
            async (ev, t) =>
            {
                // If request already shutting down, ignore.
                if (t.IsCancellationRequested)
                    return;

                await TryWriteEventAsync(ev, t);

                if (IsTerminal(ev.Stage))
                    await WriteDoneNowAsync(ev, t);
            },
            token);

        // 2) Replay stored events AFTER subscribing
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

        // 3) Keep connection open until completion/disconnect
        try
        {
            while (!token.IsCancellationRequested)
                await Task.Delay(500, token);
        }
        catch (OperationCanceledException)
        {
            // expected (completion or client disconnect)
        }
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