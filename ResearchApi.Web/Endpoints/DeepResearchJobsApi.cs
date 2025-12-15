using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ResearchApi.Domain;

public static class DeepResearchJobsApi
{
    public static void MapDeepResearchJobsApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/research")
            .WithTags("Deep Research Jobs API")
            .RequireAuthorization();

        api.MapPost("/jobs", CreateJobAsync);

        api.MapGet("/jobs/{jobId:guid}", GetJobAsync);

        api.MapGet("/jobs/{jobId:guid}/events", ListEventsAsync);

        api.MapGet("/jobs/{jobId:guid}/events/stream", StreamEventsAsync);
    }

  private static async Task<IResult> CreateJobAsync(
        HttpContext httpContext,
        [FromBody] CreateResearchJobRequest request,
        IResearchOrchestrator orchestrator,
        IResearchProtocolService protocolService,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        // Optional services (present only when Webhook section is configured)
        var webhookStore = httpContext.RequestServices.GetService<IWebhookSubscriptionStore>();
        var webhookDispatcher = httpContext.RequestServices.GetService<IWebhookDispatcher>();
        var streamingConfigured = httpContext.RequestServices.GetService<IResearchEventBus>() is not null;

        var warnings = new List<string>();
        // If breadth/depth/language/region not provided, compute them via protocol service
        int? breadth = request.Breadth;
        int? depth = request.Depth;
        string? language = request.Language;
        string? region = request.Region;

        if (!breadth.HasValue || !depth.HasValue || string.IsNullOrEmpty(language))
        {
            var clarifications = request.Clarifications?.Select(c => new Clarification {
                Question = c.Question,
                Answer = c.Answer
            }).ToList() ?? new List<Clarification>();

            if (!breadth.HasValue || !depth.HasValue)
            {
                (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(request.Query, clarifications, ct);
            }

            if (string.IsNullOrEmpty(language))
            {
                (language, region) = await protocolService.AutoSelectLanguageRegionAsync(request.Query, clarifications, ct);
            }
        }

        // Create the job
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

        // Trigger the job execution in background
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

        if (request.Webhook is not null)
        {
            await HandleWebhookRequestedAsync(
                jobId: job.Id,
                createdUtc: job.CreatedAt,
                webhook: request.Webhook,
                webhookStore: webhookStore,
                webhookDispatcher: webhookDispatcher,
                warnings: warnings,
                ct: ct);
        }

        // Build links; omit stream if not configured
        var links = new Dictionary<string, string>
        {
            ["self"] = $"/api/research/jobs/{job.Id}",
            ["events"] = $"/api/research/jobs/{job.Id}/events"
        };

        if (streamingConfigured)
        {
            links["stream"] = $"/api/research/jobs/{job.Id}/events/stream";
        }
        else
        {
            warnings.Add("Live event streaming is not configured on this server. Use the /events endpoint to poll events.");
        }

        var response = new
        {
            jobId = job.Id,
            status = job.Status.ToString(),
            createdUtc = job.CreatedAt,
            links,
            warnings = warnings.Count == 0 ? null : warnings
        };

        return Results.Ok(response);
    }

    private static async Task<IResult> GetJobAsync(
        Guid jobId,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job == null)
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
        var events = await jobStore.GetEventsAsync(jobId, ct);
        if (events == null)
            return Results.NotFound();

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
        CancellationToken ct)
    {
        // If streaming/event bus isn't configured, return 404 (endpoint exists but feature disabled)
        var eventBus = httpContext.RequestServices.GetService<IResearchEventBus>();
        if (eventBus is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Validate job exists
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ConfigureSseHeaders(httpContext);

        var jsonOptions = CreateJsonOptions();
        var lastEventId = GetLastEventId(httpContext);
        var storedEvents = await jobStore.GetEventsAsync(jobId, ct);

        var replayStart = FindReplayStartIndex(storedEvents, lastEventId, out var lastEventIdWasFound);

        if (!string.IsNullOrWhiteSpace(lastEventId) && !lastEventIdWasFound)
        {
            await WriteSseAsync(
                httpContext,
                jsonOptions,
                eventName: "error",
                id: "replay",
                data: new
                {
                    message = "Last-Event-ID was not found in stored events; replaying full history.",
                    lastEventId
                },
                ct);
        }

        var terminalStage = await ReplayStoredEventsAsync(httpContext, jsonOptions, jobId, storedEvents, replayStart, ct);
        if (terminalStage is ResearchEventStage.Completed or ResearchEventStage.Failed)
        {
            await WriteDoneAsync(httpContext, jsonOptions, jobId, terminalStage.Value, ct);
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, httpContext.RequestAborted);
        var token = linkedCts.Token;

        var doneTcs = new TaskCompletionSource<ResearchEventStage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var subscription = await eventBus.SubscribeAsync(
            jobId,
            (ev, t) => OnLiveEventAsync(httpContext, jsonOptions, jobId, ev, doneTcs, t),
            token);

        await WaitForCompletionOrDisconnectAsync(httpContext, doneTcs, token);

        if (doneTcs.Task.IsCompletedSuccessfully && !token.IsCancellationRequested)
        {
            await WriteDoneAsync(httpContext, jsonOptions, jobId, doneTcs.Task.Result, token);
        }
    }

    private static readonly ResearchEventStage[] DefaultWebhookStages = new[]
    {
        ResearchEventStage.Planning,
        ResearchEventStage.Summarizing,
        ResearchEventStage.Searching,
        ResearchEventStage.LearningExtraction,
        ResearchEventStage.Metrics,
        ResearchEventStage.Completed,
        ResearchEventStage.Failed
    };

    private static async Task HandleWebhookRequestedAsync(
        Guid jobId,
        DateTimeOffset createdUtc,
        WebhookDto webhook,
        IWebhookSubscriptionStore? webhookStore,
        IWebhookDispatcher? webhookDispatcher,
        List<string> warnings,
        CancellationToken ct)
    {
        if (webhookStore is null || webhookDispatcher is null)
        {
            warnings.Add("Webhook was requested but webhooks are not configured on this server. No webhook will be delivered.");
            return;
        }

        if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out var uri))
        {
            warnings.Add("Webhook URL is invalid; webhook subscription was not saved.");
            return;
        }

        var subscription = new WebhookSubscription(
            JobId: jobId,
            Url: uri,
            Secret: string.IsNullOrWhiteSpace(webhook.Secret) ? null : webhook.Secret,
            Stages: DefaultWebhookStages,
            CreatedUtc: DateTimeOffset.UtcNow);

        await webhookStore.SaveAsync(subscription, ct);

        // Enqueue a Created notification immediately
        var createdDelivery = new WebhookDeliveryRequest(
            JobId: jobId,
            Stage: ResearchEventStage.Created,
            TimestampUtc: DateTimeOffset.UtcNow,
            Data: new { jobId, status = "Queued", created = createdUtc });

        await webhookDispatcher.EnqueueAsync(createdDelivery, ct);
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

    private static string GetLastEventId(HttpContext httpContext)
        => httpContext.Request.Headers["Last-Event-ID"].ToString();

    private static int FindReplayStartIndex(
        IReadOnlyList<ResearchEvent> events,
        string lastEventId,
        out bool found)
    {
        found = false;

        if (string.IsNullOrWhiteSpace(lastEventId))
            return 0;

        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].Id.ToString().Equals(lastEventId, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return i + 1;
            }
        }

        return 0; // not found -> replay all
    }

    private static async Task<ResearchEventStage?> ReplayStoredEventsAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        Guid jobId,
        IReadOnlyList<ResearchEvent> events,
        int startIndex,
        CancellationToken ct)
    {
        for (var i = startIndex; i < events.Count; i++)
        {
            var e = events[i];

            await WriteSseAsync(
                httpContext,
                jsonOptions,
                eventName: "event",
                id: e.Id.ToString(),
                data: new { id = e.Id, timestamp = e.Timestamp, stage = e.Stage, message = e.Message },
                ct);

            if (e.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
                return e.Stage;
        }

        return null;
    }

    private static async Task OnLiveEventAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        Guid jobId,
        ResearchEvent ev,
        TaskCompletionSource<ResearchEventStage> doneTcs,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        await WriteSseAsync(
            httpContext,
            jsonOptions,
            eventName: "event",
            id: ev.Id.ToString(),
            data: new { id = ev.Id, timestamp = ev.Timestamp, stage = ev.Stage, message = ev.Message },
            ct);

        if (ev.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
            doneTcs.TrySetResult(ev.Stage);
    }

    private static async Task WaitForCompletionOrDisconnectAsync(
        HttpContext httpContext,
        TaskCompletionSource<ResearchEventStage> doneTcs,
        CancellationToken ct)
    {
        const bool EnableHeartbeat = true;
        var heartbeatInterval = TimeSpan.FromSeconds(20);

        var lastHeartbeat = DateTimeOffset.UtcNow;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (doneTcs.Task.IsCompleted)
                    return;

                if (EnableHeartbeat && DateTimeOffset.UtcNow - lastHeartbeat >= heartbeatInterval)
                {
                    await WriteHeartbeatAsync(httpContext, ct);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                // Wake early if done arrives; otherwise tick every 1s
                var delayTask = Task.Delay(TimeSpan.FromSeconds(1), ct);
                var completed = await Task.WhenAny(doneTcs.Task, delayTask);
                if (completed == doneTcs.Task)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // disconnect/cancel
        }
    }

    private static async Task WriteDoneAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        Guid jobId,
        ResearchEventStage stage,
        CancellationToken ct)
    {
        await WriteSseAsync(
            httpContext,
            jsonOptions,
            eventName: "done",
            id: "done",
            data: new { jobId, status = stage.ToString() },
            ct);
    }

    private static async Task WriteHeartbeatAsync(HttpContext httpContext, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(": heartbeat\n\n");
        await httpContext.Response.Body.WriteAsync(bytes, ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }

    private static async Task WriteSseAsync(
        HttpContext httpContext,
        JsonSerializerOptions jsonOptions,
        string eventName,
        string id,
        object data,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("id: ").Append(id).Append('\n');
        sb.Append("event: ").Append(eventName).Append('\n');
        sb.Append("data: ").Append(JsonSerializer.Serialize(data, jsonOptions)).Append("\n\n");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await httpContext.Response.Body.WriteAsync(bytes, ct);
        await httpContext.Response.Body.FlushAsync(ct);
    }
}
