using System.Text;
using ResearchApi.Domain;

public static class OpenAiModelEndpoints
{
    public sealed class ChatRequest { public string Model { get; set; } = default!; public bool Stream { get; set; } = true; public List<ChatMessage> Messages { get; set; } = new(); }
    public sealed class ChatMessage { public string Role { get; set; } = default!; public string Content { get; set; } = default!; }

    public static void MapResearchModel(this WebApplication app)
    {
        app.MapGet("/v1/models", HandleGetModels);
        app.MapPost("/v1/chat/completions", HandleChatCompletionsAsync);
    }

    private static IResult HandleGetModels()
    {
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var response = new
        {
            // OpenAI-style list wrapper
            @object = "list",
            data = new[]
            {
                new
                {
                    id      = "Open Deep Research", 
                    @object = "model",
                    created = created,
                    owned_by = "local",
                    description = "Open Deep Research wrapper model"
                }
            }
        };

        return Results.Json(response);
    }

    private static async Task HandleChatCompletionsAsync(
        HttpContext httpContext,
        ChatRequest request,
        IResearchOrchestrator orchestrator,
        IResearchJobStore jobStore,
        IChatModel chatModel,
        IResearchProtocolService protocolService,
        IResearchEventBus eventBus,
        CancellationToken ct)
    {
        ConfigureOpenAiSseHeaders(httpContext);

        var sse    = new SseFrameWriter(httpContext);
        var writer = new OpenAiChatStreamWriter(sse);

        // Flush early so clients (Open-WebUI) switch into streaming mode
        await sse.FlushAsync(ct);

        var requestId = $"ldr-{Guid.NewGuid():N}";
        var created   = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var modelName = string.IsNullOrWhiteSpace(request.Model) ? "local-deep-research" : request.Model;

        // Fail fast: this endpoint is streaming-only; no polling fallback
        if (eventBus is null)
        {
            await writer.WriteErrorAsync(
                requestId, created, modelName,
                "Live event streaming is not configured on this server (IResearchEventBus is missing). " +
                "Enable IResearchEventBus or use /api/research/jobs/{jobId}/events to poll.",
                ct);
            return;
        }

        // ----------------- базовая валидация -------------------
        if (request.Messages is null || request.Messages.Count == 0)
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "No messages supplied.", ct);
            return;
        }

        var (breadthOverride, depthOverride) = ResearchProtocolHelper.ExtractBreadthDepthFromMessages(request.Messages);
        var (langOverride, regionOverride)   = ResearchProtocolHelper.ExtractLanguageRegionFromMessages(request.Messages);

        var configureRequested = request.Messages.Any(m =>
            !string.IsNullOrWhiteSpace(m.Content) &&
            m.Content.Contains("/configure", StringComparison.OrdinalIgnoreCase));

        var userMessages = request.Messages
            .Select((m, idx) => new { m, idx })
            .Where(x => x.m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (userMessages.Count == 0)
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "No user message provided.", ct);
            return;
        }

        var firstUser    = userMessages.First();
        var initialQuery = firstUser.m.Content?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(initialQuery))
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "Initial user message is empty.", ct);
            return;
        }

        // Find the latest assistant message that contains the clarification block
        var assistantWithClar = request.Messages
            .Select((m, idx) => new { m, idx })
            .LastOrDefault(x =>
                x.m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
                x.m.Content != null &&
                x.m.Content.Contains(ResearchProtocolHelper.ClarificationsBeginMarker, StringComparison.Ordinal));

        var hasClarificationBlock = assistantWithClar != null;

        bool hasUserAfterClar = false;
        if (assistantWithClar != null)
        {
            var clarIdx = assistantWithClar.idx;
            hasUserAfterClar = userMessages.Any(um => um.idx > clarIdx);
        }

        // =========================================================
        // PHASE 1: no block yet -> ask clarification questions
        // =========================================================
        if (!hasClarificationBlock)
        {
            var questions = await protocolService.GenerateFeedbackQueriesAsync(initialQuery, configureRequested, ct);

            var sb = new StringBuilder();
            sb.AppendLine("To better focus the research, please answer the following clarification questions:");
            sb.AppendLine();
            sb.AppendLine(ResearchProtocolHelper.ClarificationsBeginMarker);
            for (int i = 0; i < questions.Count; i++)
                sb.AppendLine($"{i + 1}. {questions[i]}");
            sb.AppendLine(ResearchProtocolHelper.ClarificationsEndMarker);
            sb.AppendLine();
            sb.AppendLine("You can answer them in a numbered list, for example:");
            sb.AppendLine("1) ...");
            sb.AppendLine("2) ...");
            sb.AppendLine("3) ...");

            await writer.WriteTextDeltaAsync(requestId, created, modelName, role: "assistant", content: sb.ToString(), finishReason: null, ct);
            await writer.WriteDoneAsync(ct);
            return;
        }

        // =========================================================
        // PHASE 2: clarification block exists but user hasn't answered yet
        // =========================================================
        if (!hasUserAfterClar)
        {
            await writer.WriteTextDeltaAsync(
                requestId, created, modelName,
                role: "assistant",
                content: assistantWithClar!.m.Content ?? "",
                finishReason: null,
                ct);

            await writer.WriteDoneAsync(ct);
            return;
        }

        // Parse questions from assistant block
        var clarificationQuestions = ResearchProtocolHelper.ExtractQuestionsFromContent(assistantWithClar!.m.Content ?? "");

        // Latest user message after the clarification block is treated as answers
        var answerUser   = userMessages.Last(um => um.idx > assistantWithClar.idx);
        var answersText  = answerUser.m.Content?.Trim() ?? "";
        var parsedAnswers = ResearchProtocolHelper.ParseAnswersFromUserText(answersText);

        var clarifications = new List<Clarification>();

        if (clarificationQuestions.Count > 0)
        {
            for (int i = 0; i < clarificationQuestions.Count; i++)
            {
                var q = clarificationQuestions[i];
                var a = (i < parsedAnswers.Count && !string.IsNullOrWhiteSpace(parsedAnswers[i]))
                    ? parsedAnswers[i]
                    : answersText; // fallback to full text

                clarifications.Add(new Clarification { Question = q, Answer = a });
            }
        }
        else
        {
            clarifications.Add(new Clarification { Question = "User clarifications", Answer = answersText });
        }

        // ---------- choose breadth/depth ----------
        int breadth, depth;
        if (breadthOverride.HasValue || depthOverride.HasValue)
        {
            breadth = Math.Clamp(breadthOverride ?? 2, 1, 8);
            depth   = Math.Clamp(depthOverride ?? 2, 1, 4);
        }
        else
        {
            (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(initialQuery, clarifications, ct);
        }

        // ---------- choose language/region ----------
        string language;
        string? region;

        if (!string.IsNullOrWhiteSpace(langOverride) || !string.IsNullOrWhiteSpace(regionOverride))
        {
            language = langOverride ?? "en";
            region   = regionOverride;
        }
        else
        {
            (language, region) = await protocolService.AutoSelectLanguageRegionAsync(initialQuery, clarifications, ct);
        }

        // ---------- emit header chunk ----------
        await writer.BeginThinkAsync(requestId, created, modelName, ct);
        var thinkHeader = BuildThinkHeader(initialQuery, breadth, depth, language, region, clarifications);
        await writer.WriteThinkAsync(requestId, created, modelName, thinkHeader, ct);

        // ---------- start job ----------
        var job = await orchestrator.StartJobAsync(
            initialQuery,
            clarifications,
            breadth: breadth,
            depth: depth,
            language: language,
            region: region,
            ct);

        _ = Task.Run(async () =>
        {
            try { await orchestrator.RunJobAsync(job.Id, CancellationToken.None); }
            catch (Exception ex) { Console.WriteLine($"[deep-research-model] Error in RunJobAsync: {ex}"); }
        });

        // ---------- stream events via bus (replay + live) ----------
        await StreamViaEventBusAsync(
            httpContext,
            requestId,
            created,
            modelName,
            writer,
            jobStore,
            eventBus,
            job.Id,
            ct);

        await writer.EndThinkAsync(requestId, created, modelName, ct);

        // ---------- final report ----------
        var currentJob = await jobStore.GetJobAsync(job.Id, ct);
        if (currentJob != null)
        {
            var statusStr = currentJob.Status.ToString();
            var rawReport = currentJob.ReportMarkdown ?? "_No reportMarkdown was generated by the research API._";
            var report    = chatModel.StripThinkBlock(rawReport);

            var finalContent =
                $"### Open Deep Research\n\n" +
                $"**Job ID:** `{currentJob.Id}`  \n" +
                $"**Status:** `{statusStr}`  \n" +
                $"**Query:** {currentJob.Query}\n\n" +
                report;

            await writer.WriteTextDeltaAsync(
                requestId, created, modelName,
                role: null,
                content: finalContent,
                finishReason: null,
                ct);
        }

        await writer.WriteStopAsync(requestId, created, modelName, ct); 
        await writer.WriteDoneAsync(ct);
    }

    private static void ConfigureOpenAiSseHeaders(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers["Content-Type"]  = "text/event-stream";
        httpContext.Response.Headers["Cache-Control"] = "no-cache";
        httpContext.Response.Headers["Connection"]    = "keep-alive";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
    }

    private static string BuildThinkHeader(
        string initialQuery, int breadth, int depth, string language, string? region,
        List<Clarification> clarifications)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Starting local deep research with clarifications.");
        sb.AppendLine();
        sb.AppendLine($"User query: \"{initialQuery}\"");
        sb.AppendLine();
        sb.AppendLine($"Breadth: {breadth}, Depth: {depth}");
        sb.AppendLine();
        sb.AppendLine($"Region: {region}, Language: {language}");
        sb.AppendLine();
        sb.AppendLine("Clarifications:");
        for (int i = 0; i < clarifications.Count; i++)
        {
            sb.AppendLine($"Q{i + 1}: {clarifications[i].Question}");
            sb.AppendLine($"A{i + 1}: {clarifications[i].Answer}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static async Task StreamViaEventBusAsync(
        HttpContext httpContext,
        string requestId, long created, string modelName,
        OpenAiChatStreamWriter writer,
        IResearchJobStore jobStore,
        IResearchEventBus eventBus,
        Guid jobId,
        CancellationToken ct)
    {
        // 1) Replay stored events so the client sees progress immediately
        var storedEvents = await jobStore.GetEventsAsync(jobId, ct);
        foreach (var ev in storedEvents)
        {
            await writer.WriteTextDeltaAsync(
                requestId, created, modelName,
                role: null,
                content: $"{ev.Stage}: {ev.Message}\n",
                finishReason: null,
                ct);

            if (ev.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
                return;
        }

        // 2) Subscribe to live events
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, httpContext.RequestAborted);
        var token = linkedCts.Token;

        var doneTcs = new TaskCompletionSource<ResearchEventStage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var sub = await eventBus.SubscribeAsync(
            jobId,
            async (ev, t) =>
            {
                if (t.IsCancellationRequested) return;
                
                await writer.WriteThinkAsync(
                    requestId,
                    created,
                    modelName,
                    $"{ev.Stage}: {ev.Message}\n", 
                    ct
                );

                if (ev.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
                    doneTcs.TrySetResult(ev.Stage);
            },
            token);

        // 3) Wait for completion OR disconnect, with heartbeat
        var heartbeatEvery = TimeSpan.FromSeconds(20);
        var lastBeat = DateTimeOffset.UtcNow;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (doneTcs.Task.IsCompleted)
                    return;

                if (DateTimeOffset.UtcNow - lastBeat >= heartbeatEvery)
                {
                    // comment heartbeat keeps SSE connections alive through proxies
                    var sse = new SseFrameWriter(httpContext);
                    await sse.WriteCommentAsync("heartbeat", token);
                    lastBeat = DateTimeOffset.UtcNow;
                }

                var delay = Task.Delay(TimeSpan.FromSeconds(1), token);
                await Task.WhenAny(doneTcs.Task, delay);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
    }
}
