using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ResearchEngine.Domain;

namespace ResearchEngine.API;

public static partial class ResearchApi
{
    /// <summary>
    /// GET /api/jobs/{jobId}/events
    /// Lists persisted events for a job.
    /// </summary>
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

    /// <summary>
    /// POST /api/jobs/{jobId}/events/stream-token
    /// Mints a short-lived ticket for opening the SSE stream via EventSource.
    /// </summary>
    private static async Task<IResult> CreateEventsStreamTokenAsync(
        Guid jobId,
        HttpContext httpContext,
        ClaimsPrincipal user,
        IResearchJobRepository jobRepository,
        IJobSseTicketService tickets,
        CancellationToken ct)
    {
        var job = await jobRepository.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

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
    /// GET /api/jobs/{jobId}/events/stream
    /// Server-sent events stream of job events (replay + live).
    /// </summary>
    private static async Task StreamEventsAsync(
        HttpContext httpContext,
        Guid jobId,
        IResearchJobRepository jobRepository,
        IResearchEventRepository eventRepository,
        IResearchEventBus eventBus,
        IJobSseTicketService tickets,
        CancellationToken ct)
    {
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
