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
        api.MapGet("/jobs/{jobId:guid}/events", ListEventsAsync);
        api.MapGet("/jobs/{jobId:guid}/events/stream", StreamEventsAsync);
    }

    private static async Task<IResult> CreateJobAsync(
        [FromBody] CreateResearchJobRequest request,
        IResearchOrchestrator orchestrator,
        IResearchProtocolService protocolService,
        IResearchJobStore jobStore,
        IWebhookSubscriptionStore webhookStore,
        IWebhookDispatcher webhookDispatcher,
        IResearchEventBus eventBus,
        CancellationToken ct)
    {
        // Compute missing protocol params
        int? breadth = request.Breadth;
        int? depth = request.Depth;
        string? language = request.Language;
        string? region = request.Region;

        if (!breadth.HasValue || !depth.HasValue || string.IsNullOrEmpty(language))
        {
            var clarifications = request.Clarifications?.Select(c => new Clarification
            {
                Question = c.Question,
                Answer = c.Answer
            }).ToList() ?? new List<Clarification>();

            if (!breadth.HasValue || !depth.HasValue)
                (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(request.Query, clarifications, ct);

            if (string.IsNullOrEmpty(language))
                (language, region) = await protocolService.AutoSelectLanguageRegionAsync(request.Query, clarifications, ct);
        }

        // Create job
        var job = await orchestrator.StartJobAsync(
            request.Query,
            request.Clarifications?.Select(c => new Clarification
            {
                Question = c.Question,
                Answer = c.Answer
            }) ?? [],
            breadth ?? 2,
            depth ?? 2,
            language ?? "en",
            region,
            ct);

        // Persist webhook subscription if provided
        if (request.Webhook is not null)
        {
            await SaveWebhookSubscriptionAsync(job.Id, request.Webhook, webhookStore, ct);

            // Optional: immediate "Created" webhook delivery
            await webhookDispatcher.EnqueueAsync(
                new WebhookDeliveryRequest(
                    job.Id,
                    ResearchEventStage.Created,
                    DateTimeOffset.UtcNow,
                    new { jobId = job.Id, status = "Queued", created = job.CreatedAt }),
                ct);
        }

        // Trigger job execution in background
        _ = Task.Run(async () =>
        {
            try
            {
                await orchestrator.RunJobAsync(job.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[deep-research-jobs] Error in RunJobAsync: {ex}");
            }
        });

        var response = new
        {
            jobId = job.Id,
            status = job.Status.ToString(),
            createdUtc = job.CreatedAt,
            links = new
            {
                self = $"/api/research/jobs/{job.Id}",
                events = $"/api/research/jobs/{job.Id}/events",
                stream = $"/api/research/jobs/{job.Id}/events/stream"
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

        var response = new
        {
            id = job.Id,
            query = job.Query,
            breadth = job.Breadth,
            depth = job.Depth,
            status = job.Status.ToString(),
            targetLanguage = job.TargetLanguage,
            region = job.Region,
            reportMarkdown = job.ReportMarkdown,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt,
            clarifications = job.Clarifications.Select(c => new { c.Question, c.Answer }),
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> ListEventsAsync(
        Guid jobId,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        // (Optional) validate job exists
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
        // Validate job exists
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ConfigureSseHeaders(httpContext);

        var jsonOptions = CreateJsonOptions();

        // Last-Event-ID is the *int* ResearchEvent.Id
        var lastId = GetLastEventIdAsInt(httpContext);

        // Replay from store (id > lastId)
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
            // Wait until Completed/Failed, or client disconnects
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
