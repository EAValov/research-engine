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
        IWebhookSubscriptionStore webhookStore,
        IWebhookDispatcher webhookDispatcher,
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

        // Persist webhook subscription if provided
        if (request.Webhook is not null)
        {
            await SaveWebhookSubscriptionAsync(jobId, request.Webhook, webhookStore, ct);

            // Optional: immediate "Created" webhook delivery
            await webhookDispatcher.EnqueueAsync(
                new WebhookDeliveryRequest(
                    jobId,
                    ResearchEventStage.Created,
                    DateTimeOffset.UtcNow,
                    new { jobId, status = "Queued" }),
                ct);
        }

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
            count = learnings.Count,
            learnings = learnings.Select(l => new
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

        return Results.Ok(new
        {
            jobId,
            synthesis = new
            {
                id = syn.Id,
                status = syn.Status.ToString(),
                outline = syn.Outline,
                instructions = syn.Instructions,
                reportMarkdown = syn.ReportMarkdown,
                createdAt = syn.CreatedAt,
                completedAt = syn.CompletedAt,
                errorMessage = syn.ErrorMessage
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

        return Results.Ok(new
        {
            id = syn.Id,
            jobId = syn.JobId,
            parentSynthesisId = syn.ParentSynthesisId,
            status = syn.Status.ToString(),
            outline = syn.Outline,
            instructions = syn.Instructions,
            reportMarkdown = syn.ReportMarkdown,
            createdAt = syn.CreatedAt,
            completedAt = syn.CompletedAt,
            errorMessage = syn.ErrorMessage
        });
    }

    // ---------------- events (unchanged) ----------------

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

        var storedEvents = await jobStore.GetEventsAsync(jobId, ct);
        foreach (var e in storedEvents.Where(e => e.Id > lastId))
        {
            await WriteEventAsync(httpContext, jsonOptions, e, ct);

            if (e.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
            {
                await WriteDoneAsync(httpContext, jsonOptions, jobId, e.Stage, ct);
                return;
            }
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, httpContext.RequestAborted);
        var token = linkedCts.Token;

        var doneTcs = new TaskCompletionSource<ResearchEventStage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await eventBus.SubscribeAsync(
            jobId,
            (ev, t) => OnLiveEventAsync(httpContext, jsonOptions, jobId, ev, doneTcs, t),
            token);

        await WaitForCompletionOrDisconnectAsync(doneTcs, token);

        if (doneTcs.Task.IsCompletedSuccessfully && !token.IsCancellationRequested)
        {
            await WriteDoneAsync(httpContext, jsonOptions, jobId, doneTcs.Task.Result, token);
        }
    }

    // ---------------- helpers ----------------

    private static async Task SaveWebhookSubscriptionAsync(
        Guid jobId,
        WebhookDto webhook,
        IWebhookSubscriptionStore store,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Invalid webhook Url.", nameof(webhook));

        var stages = new[]
        {
            ResearchEventStage.Created,
            ResearchEventStage.Planning,
            ResearchEventStage.Summarizing,
            ResearchEventStage.Searching,
            ResearchEventStage.LearningExtraction,
            ResearchEventStage.Metrics,
            ResearchEventStage.Completed,
            ResearchEventStage.Failed
        };

        var sub = new WebhookSubscription(
            JobId: jobId,
            Url: uri,
            Secret: string.IsNullOrWhiteSpace(webhook.Secret) ? null : webhook.Secret,
            Stages: stages,
            CreatedUtc: DateTimeOffset.UtcNow);

        await store.SaveAsync(sub, ct);
    }

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

    private static Task WriteDoneAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        Guid jobId,
        ResearchEventStage status,
        CancellationToken token)
        => WriteSseAsync(
            httpContext,
            jsonOptions,
            eventName: "done",
            id: "done",
            data: new { jobId, status = status.ToString() },
            token: token);

    private static async Task OnLiveEventAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        Guid jobId,
        ResearchEvent ev,
        TaskCompletionSource<ResearchEventStage> doneTcs,
        CancellationToken token)
    {
        await WriteEventAsync(httpContext, jsonOptions, ev, token);

        if (ev.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
        {
            doneTcs.TrySetResult(ev.Stage);
        }
    }

    private static async Task WaitForCompletionOrDisconnectAsync(
        TaskCompletionSource<ResearchEventStage> doneTcs,
        CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !doneTcs.Task.IsCompleted)
            {
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
    }
}