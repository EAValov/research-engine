using System.Text;
using System.Text.RegularExpressions;
using ResearchApi.Domain;

public static class OpenAiModelEndpoints
{
    public sealed class ChatRequest
    {
        public string Model { get; set; } = default!;
        public bool Stream { get; set; } = true;
        public List<ChatMessage> Messages { get; set; } = new();
    }

    public sealed class ChatMessage
    {
        public string Role { get; set; } = default!;
        public string Content { get; set; } = default!;
    }

    private const string JobIdMarkerPrefix = "[LOCAL_DEEP_RESEARCH_JOB_ID=";
    private static readonly Regex JobIdMarkerRegex =
        new(@"\[LOCAL_DEEP_RESEARCH_JOB_ID=([0-9a-fA-F\-]{36})\]",
            RegexOptions.Compiled);

    private static readonly Regex JobIdHtmlCommentRegex =
        new(@"<!--\s*LOCAL_DEEP_RESEARCH_JOB_ID=([0-9a-fA-F\-]{36})\s*-->",
            RegexOptions.Compiled);

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
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "Open Deep Research",
                    @object = "model",
                    created,
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
        IReportSynthesisService synthesisService,
        IChatModel chatModel,
        IResearchProtocolService protocolService,
        IResearchEventBus eventBus,
        CancellationToken ct)
    {
        ConfigureOpenAiSseHeaders(httpContext);

        var sse = new SseFrameWriter(httpContext);
        var writer = new OpenAiChatStreamWriter(sse);

        await sse.FlushAsync(ct);

        var requestId = $"ldr-{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var modelName = string.IsNullOrWhiteSpace(request.Model) ? "local-deep-research" : request.Model;

        if (request.Messages is null || request.Messages.Count == 0)
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "No messages supplied.", ct);
            return;
        }

        var lastUser = request.Messages.LastOrDefault(m => IsRole(m, "user"));
        if (lastUser is null || string.IsNullOrWhiteSpace(lastUser.Content))
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "No user message provided.", ct);
            return;
        }

        // One chat = one job
        var existingJobId = TryExtractExistingJobId(request.Messages);
        var hasExistingJob = existingJobId.HasValue;

        // Parse optional regeneration command
        var userText = lastUser.Content.Trim();
        var cmd = ParseCommand(userText);

        // If job exists and message is NOT a command -> discuss report via base model
        if (hasExistingJob && cmd.Kind == CommandKind.None)
        {
            await HandleReportDiscussionAsync(
                requestId, created, modelName,
                request.Messages,
                existingJobId!.Value,
                jobStore,
                chatModel,
                writer,
                ct);

            await writer.WriteStopAsync(requestId, created, modelName, ct);
            await writer.WriteDoneAsync(ct);
            return;
        }

        // If the user issues /regenerate but we cannot bind to a prior job,
        // DO NOT start a new job (one chat = one job).
        if (!hasExistingJob && cmd.Kind == CommandKind.RegenerateSynthesis)
        {
            await writer.WriteErrorAsync(
                requestId, created, modelName,
                "This chat is not bound to an existing job yet, so /regenerate cannot be used here.",
                ct);
            return;
        }

        // If job exists and user asks for regeneration -> regenerate synthesis
        if (hasExistingJob && cmd.Kind == CommandKind.RegenerateSynthesis)
        {
            await HandleRegenerateSynthesisAsync(
                httpContext,
                requestId, created, modelName,
                existingJobId!.Value,
                cmd,
                jobStore,
                synthesisService,
                chatModel,
                eventBus,
                writer,
                ct);

            await writer.WriteStopAsync(requestId, created, modelName, ct);
            await writer.WriteDoneAsync(ct);
            return;
        }

        // No job exists yet -> run normal clarification flow -> start job -> stream events -> show report
        await HandleNewJobFlowAsync(
            httpContext,
            requestId, created, modelName,
            request.Messages,
            orchestrator,
            jobStore,
            chatModel,
            protocolService,
            eventBus,
            writer,
            ct);

        await writer.WriteStopAsync(requestId, created, modelName, ct);
        await writer.WriteDoneAsync(ct);
    }

    // =========================================================
    // New job flow (clarifications -> StartJobAsync -> stream -> final report)
    // =========================================================
    private static async Task HandleNewJobFlowAsync(
        HttpContext httpContext,
        string requestId, long created, string modelName,
        IReadOnlyList<ChatMessage> messages,
        IResearchOrchestrator orchestrator,
        IResearchJobStore jobStore,
        IChatModel chatModel,
        IResearchProtocolService protocolService,
        IResearchEventBus eventBus,
        OpenAiChatStreamWriter writer,
        CancellationToken ct)
    {
        var (breadthOverride, depthOverride) = ResearchProtocolHelper.ExtractBreadthDepthFromMessages(messages);
        var (langOverride, regionOverride) = ResearchProtocolHelper.ExtractLanguageRegionFromMessages(messages);

        var configureRequested = messages.Any(m =>
            !string.IsNullOrWhiteSpace(m.Content) &&
            m.Content.Contains("/configure", StringComparison.OrdinalIgnoreCase));

        var userMessages = messages
            .Select((m, idx) => new { m, idx })
            .Where(x => IsRole(x.m, "user"))
            .ToList();

        if (userMessages.Count == 0)
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "No user message provided.", ct);
            return;
        }

        var firstUser = userMessages.First();
        var initialQuery = firstUser.m.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(initialQuery))
        {
            await writer.WriteErrorAsync(requestId, created, modelName, "Initial user message is empty.", ct);
            return;
        }

        // Find the latest assistant message containing the clarification block
        var assistantWithClar = messages
            .Select((m, idx) => new { m, idx })
            .LastOrDefault(x =>
                IsRole(x.m, "assistant") &&
                x.m.Content != null &&
                x.m.Content.Contains(ResearchProtocolHelper.ClarificationsBeginMarker, StringComparison.Ordinal));

        var hasClarificationBlock = assistantWithClar != null;

        bool hasUserAfterClar = false;
        if (assistantWithClar != null)
        {
            var clarIdx = assistantWithClar.idx;
            hasUserAfterClar = userMessages.Any(um => um.idx > clarIdx);
        }

        // PHASE 1: no block yet -> ask clarification questions
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
            return;
        }

        // PHASE 2: clarification block exists but user hasn't answered yet
        if (!hasUserAfterClar)
        {
            await writer.WriteTextDeltaAsync(
                requestId, created, modelName,
                role: "assistant",
                content: assistantWithClar!.m.Content ?? "",
                finishReason: null,
                ct);
            return;
        }
        
        // Parse questions from assistant block
        var clarificationQuestions = ResearchProtocolHelper.ExtractQuestionsFromContent(assistantWithClar!.m.Content ?? "");

        // Latest user message after the clarification block is treated as answers
        var answerUser = userMessages.Last(um => um.idx > assistantWithClar.idx);
        var answersText = answerUser.m.Content?.Trim() ?? "";

        if (answersText.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            await writer.WriteTextDeltaAsync(
                requestId, created, modelName,
                role: "assistant",
                content: "Please answer the clarification questions (a numbered list). Commands like /regenerate work only after the report is produced.",
                finishReason: null,
                ct);
            return;
        }

        var parsedAnswers = ResearchProtocolHelper.ParseAnswersFromUserText(answersText);

        var clarifications = new List<Clarification>();
        if (clarificationQuestions.Count > 0)
        {
            for (int i = 0; i < clarificationQuestions.Count; i++)
            {
                var q = clarificationQuestions[i];
                var a = (i < parsedAnswers.Count && !string.IsNullOrWhiteSpace(parsedAnswers[i]))
                    ? parsedAnswers[i]
                    : answersText;

                clarifications.Add(new Clarification { Question = q, Answer = a });
            }
        }
        else
        {
            clarifications.Add(new Clarification { Question = "User clarifications", Answer = answersText });
        }

        // Choose breadth/depth
        int breadth, depth;
        if (breadthOverride.HasValue || depthOverride.HasValue)
        {
            breadth = Math.Clamp(breadthOverride ?? 2, 1, 8);
            depth = Math.Clamp(depthOverride ?? 2, 1, 4);
        }
        else
        {
            (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(initialQuery, clarifications, ct);
        }

        // Choose language/region
        string language;
        string? region;
        if (!string.IsNullOrWhiteSpace(langOverride) || !string.IsNullOrWhiteSpace(regionOverride))
        {
            language = langOverride ?? "en";
            region = regionOverride;
        }
        else
        {
            (language, region) = await protocolService.AutoSelectLanguageRegionAsync(initialQuery, clarifications, ct);
        }

        // Start streaming think
        await writer.BeginThinkAsync(requestId, created, modelName, ct);

        // Start job (creates row + starts running in background)
        var jobId = await orchestrator.StartJobAsync(
            initialQuery,
            clarifications,
            breadth: breadth,
            depth: depth,
            language: language,
            region: region,
            ct);

        var jobTag = $"job:{jobId.ToString("N")[..8]}";

        // Header 
        var header = BuildThinkHeader(jobTag, initialQuery, breadth, depth, language, region, clarifications);
        await writer.WriteThinkAsync(requestId, created, modelName, header , ct);

        // Stream job events (replay + live) into think
        await StreamViaEventBusIntoThinkAsync(
            httpContext: httpContext,
            requestId: requestId,
            created: created,
            modelName: modelName,
            writer: writer,
            jobStore: jobStore,
            eventBus: eventBus,
            jobId: jobId,
            jobTag: jobTag,
            cutoffEventId: 0,
            ct);

        await writer.EndThinkAsync(requestId, created, modelName, ct);

        // Show latest synthesis report
        var latestSyn = await jobStore.GetLatestSynthesisAsync(jobId, ct);
        var report = latestSyn?.ReportMarkdown;
        if (string.IsNullOrWhiteSpace(report))
            report = "_No report was generated by the research service._";

        report = chatModel.StripThinkBlock(report);

        var job = await jobStore.GetJobAsync(jobId, ct);
        var status = job?.Status.ToString() ?? "Unknown";
        var synIdText = latestSyn?.Id.ToString() ?? "n/a";

        var marker = $"<!-- LOCAL_DEEP_RESEARCH_JOB_ID={jobId} -->";
        var finalContent =
            $"### Open Deep Research\n\n" +
            $"**Job ID:** `{jobId}`  \n" +
            $"**Synthesis ID:** `{synIdText}`  \n" +
            $"**Status:** `{status}`  \n" +
            $"**Query:** {initialQuery}\n\n" +
            report;

        finalContent += "\n\n" + marker;

        await writer.WriteTextDeltaAsync(
            requestId, created, modelName,
            role: null,
            content: finalContent,
            finishReason: null,
            ct);
    }

    // =========================================================
    // Regenerate synthesis (same job, create synthesis row, run it, stream job events into think)
    // =========================================================
    private static async Task HandleRegenerateSynthesisAsync(
        HttpContext httpContext,
        string requestId, long created, string modelName,
        Guid jobId,
        ParsedCommand cmd,
        IResearchJobStore jobStore,
        IReportSynthesisService synthesisService,
        IChatModel chatModel,
        IResearchEventBus eventBus,
        OpenAiChatStreamWriter writer,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
        {
            await writer.WriteErrorAsync(requestId, created, modelName, $"Job {jobId} not found.", ct);
            return;
        }

        var jobTag = $"job:{jobId.ToString("N")[..8]}";

        // Parent = latest synthesis if any
        var latest = await jobStore.GetLatestSynthesisAsync(jobId, ct);
        var parentId = latest?.Id;

        // -------- cutoff: capture last stored event id BEFORE starting regen --------
        // This prevents replay from instantly seeing previous Completed/Failed and returning.
        var beforeEvents = await jobStore.GetEventsAsync(jobId, ct);
        var cutoffEventId = beforeEvents.Count == 0 ? 0 : beforeEvents[^1].Id;

        // Create synthesis row
        var synthesisId = await synthesisService.StartSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentId,
            outline: cmd.Outline,
            instructions: cmd.Instructions,
            ct: ct);

        var synShort = synthesisId.ToString("N")[..8];
        var synTag = $"syn:{synShort}";

        await writer.BeginThinkAsync(requestId, created, modelName, ct);

        await writer.WriteThinkAsync(
            requestId, created, modelName,
            $"[{jobTag}][{synTag}] Regenerating synthesis (parent={(parentId?.ToString() ?? "none")})\n",
            ct);

        // Run synthesis in background (long-running)
        _ = Task.Run(async () =>
        {
            try
            {
                await synthesisService.RunExistingSynthesisAsync(synthesisId, progress: null, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[deep-research-model] Error in RunExistingSynthesisAsync: {ex}");
            }
        });

        // Stream ONLY new events after cutoff, and stop when this synthesis finishes
        await StreamViaEventBusIntoThinkAsync(
            httpContext,
            requestId, created, modelName,
            writer,
            jobStore,
            eventBus,
            jobId,
            jobTag,
            cutoffEventId: cutoffEventId,
            ct: ct);

        await writer.EndThinkAsync(requestId, created, modelName, ct);

        // Print regenerated report for THIS synthesis id (not "latest")
        var syn = await jobStore.GetSynthesisAsync(synthesisId, ct);
        var report = syn?.ReportMarkdown;
        if (string.IsNullOrWhiteSpace(report))
            report = "_No report was generated by the synthesis service._";

        report = chatModel.StripThinkBlock(report);

        await writer.WriteTextDeltaAsync(
            requestId, created, modelName,
            role: null,
            content:
                $"### Regenerated Report\n\n" +
                $"**Job ID:** `{jobId}`  \n" +
                $"**Synthesis ID:** `{synthesisId}`\n\n" +
                report.Trim(),
            finishReason: null,
            ct);
    }

    // =========================================================
    // Discuss report (passthrough to underlying model; no new jobs, no synthesis unless /regenerate)
    // =========================================================
    private static async Task HandleReportDiscussionAsync(
        string requestId, long created, string modelName,
        IReadOnlyList<ChatMessage> messages,
        Guid jobId,
        IResearchJobStore jobStore,
        IChatModel chatModel,
        OpenAiChatStreamWriter writer,
        CancellationToken ct)
    {
        var job = await jobStore.GetJobAsync(jobId, ct);
        if (job is null)
        {
            await writer.WriteErrorAsync(requestId, created, modelName, $"Job {jobId} not found.", ct);
            return;
        }

        var latestSyn = await jobStore.GetLatestSynthesisAsync(jobId, ct);
        var report = latestSyn?.ReportMarkdown ?? "";
        report = chatModel.StripThinkBlock(report);

        var lastUser = messages.Last(m => IsRole(m, "user")).Content.Trim();
        var history = BuildChatHistoryForDiscussion(messages);

        var prompt = BuildDiscussionPrompt(report, history, lastUser);

        await writer.BeginThinkAsync(requestId, created, modelName, ct);

        var response = await chatModel.ChatAsync(prompt, tools: null, cancellationToken: ct);
        var text = chatModel.StripThinkBlock(response.Text).Trim();

        await writer.EndThinkAsync(requestId, created, modelName, ct);

        await writer.WriteTextDeltaAsync(
            requestId, created, modelName,
            role: "assistant",
            content: text,
            finishReason: null,
            ct);
    }

    // =========================================================
    // Stream job events into THINK block (replay + live)
    // =========================================================
   private static async Task StreamViaEventBusIntoThinkAsync(
        HttpContext httpContext,
        string requestId, long created, string modelName,
        OpenAiChatStreamWriter writer,
        IResearchJobStore jobStore,
        IResearchEventBus eventBus,
        Guid jobId,
        string jobTag,
        int? cutoffEventId,
        CancellationToken ct = default)
    {
        // 1) Replay only events AFTER cutoff
        var storedEvents = await jobStore.GetEventsAsync(jobId, ct);
        foreach (var ev in storedEvents.Where(e => e.Id > cutoffEventId))
        {
            await writer.WriteThinkAsync(
                requestId,
                created,
                modelName,
                $"[{jobTag}] {ev.Stage}: {ev.Message}\n",
                ct);

            if (ev.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
            {
                return;
            }
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, httpContext.RequestAborted);
        var token = linkedCts.Token;

        var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 2) Subscribe to live events and filter by cutoff (best-effort)
        await using var sub = await eventBus.SubscribeAsync(
            jobId,
            async (ev, t) =>
            {
                if (t.IsCancellationRequested)
                    return;

                // If bus can replay/out-of-order, this guards a bit:
                if (ev.Id <= cutoffEventId)
                    return;

                await writer.WriteThinkAsync(
                    requestId,
                    created,
                    modelName,
                    $"[{jobTag}] {ev.Stage}: {ev.Message}\n",
                    t);

                if (ev.Stage is ResearchEventStage.Completed or ResearchEventStage.Failed)
                {
                    doneTcs.TrySetResult(true);
                    return;
                }
            },
            token);

        // 3) Heartbeat loop
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

    // =========================================================
    // Command parsing: /regenerate (+ optional outline:/instructions:)
    // =========================================================
    private enum CommandKind
    {
        None,
        RegenerateSynthesis
    }

    private sealed record ParsedCommand(
        CommandKind Kind,
        string? Outline,
        string? Instructions);

    private static ParsedCommand ParseCommand(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return new ParsedCommand(CommandKind.None, null, null);

        if (!userText.StartsWith("/regenerate", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(CommandKind.None, null, null);

        var tail = userText.Substring("/regenerate".Length).Trim();
        if (string.IsNullOrWhiteSpace(tail))
            return new ParsedCommand(CommandKind.RegenerateSynthesis, null, null);

        string? outline = null;
        string? instructions = null;

        var outlineIdx = IndexOfHeader(tail, "outline:");
        var instrIdx = IndexOfHeader(tail, "instructions:");

        if (outlineIdx < 0 && instrIdx < 0)
        {
            // Fallback: treat remainder as instructions
            instructions = tail.Trim();
            return new ParsedCommand(CommandKind.RegenerateSynthesis, null, instructions);
        }

        if (outlineIdx >= 0 && (instrIdx < 0 || outlineIdx < instrIdx))
        {
            outline = ExtractBlock(tail, outlineIdx, "outline:", instrIdx);
            if (instrIdx >= 0)
                instructions = ExtractBlock(tail, instrIdx, "instructions:", endIdx: null);
        }
        else if (instrIdx >= 0)
        {
            instructions = ExtractBlock(tail, instrIdx, "instructions:", outlineIdx);
            if (outlineIdx >= 0)
                outline = ExtractBlock(tail, outlineIdx, "outline:", endIdx: null);
        }

        outline = NormalizeOptional(outline);
        instructions = NormalizeOptional(instructions);

        return new ParsedCommand(CommandKind.RegenerateSynthesis, outline, instructions);

        static int IndexOfHeader(string text, string header)
            => text.IndexOf(header, StringComparison.OrdinalIgnoreCase);

        static string ExtractBlock(string text, int startIdx, string header, int? endIdx)
        {
            var start = startIdx + header.Length;
            var end = (endIdx.HasValue && endIdx.Value > start) ? endIdx.Value : text.Length;
            return text[start..end].Trim();
        }

        static string? NormalizeOptional(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    // =========================================================
    // Job id detection in transcript
    // =========================================================
    private static Guid? TryExtractExistingJobId(IReadOnlyList<ChatMessage> messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var content = messages[i]?.Content;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var m1 = JobIdHtmlCommentRegex.Match(content);
            if (m1.Success && Guid.TryParse(m1.Groups[1].Value, out var id1))
                return id1;

            var m2 = JobIdMarkerRegex.Match(content);
            if (m2.Success && Guid.TryParse(m2.Groups[1].Value, out var id2))
                return id2;
        }

        return null;
    }
    // =========================================================
    // Discussion prompt
    // =========================================================
    private static Prompt BuildDiscussionPrompt(string reportMarkdown, string history, string userMessage)
    {
        const int maxReportChars = 45_000;
        if (reportMarkdown.Length > maxReportChars)
            reportMarkdown = reportMarkdown[..maxReportChars];

        var system = new StringBuilder();
        system.AppendLine("You are a helpful assistant discussing a research report generated by a local deep research system.");
        system.AppendLine("This chat is bound to the existing job and report; do NOT start a new research job.");
        system.AppendLine("If the user asks to regenerate the report, instruct them to use /regenerate.");
        system.AppendLine();
        system.AppendLine("Report (context):");
        system.AppendLine(reportMarkdown);

        var user = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(history))
        {
            user.AppendLine("Conversation so far:");
            user.AppendLine(history.Trim());
            user.AppendLine();
        }

        user.AppendLine("User message:");
        user.AppendLine(userMessage);

        return new Prompt(system.ToString(), user.ToString());
    }

    private static string BuildChatHistoryForDiscussion(IReadOnlyList<ChatMessage> messages)
    {
        const int maxMessages = 12;

        var tail = messages
            .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(maxMessages)
            .ToList();

        var sb = new StringBuilder();
        foreach (var m in tail)
        {
            var role = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role.Trim().ToLowerInvariant();
            var content = m.Content.Trim();

            // Remove our marker from visible history
            content = JobIdMarkerRegex.Replace(content, "").Trim();

            if (string.IsNullOrWhiteSpace(content))
                continue;

            sb.AppendLine($"{role}: {content}");
        }

        return sb.ToString().Trim();
    }

    // =========================================================
    // Think header
    // =========================================================
    private static string BuildThinkHeader(
        string jobTag,
        string initialQuery,
        int breadth,
        int depth,
        string language,
        string? region,
        List<Clarification> clarifications)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{jobTag}] Starting local deep research with clarifications.");
        sb.AppendLine();
        sb.AppendLine($"[{jobTag}] User query: \"{initialQuery}\"");
        sb.AppendLine($"[{jobTag}] Breadth: {breadth}, Depth: {depth}");
        sb.AppendLine($"[{jobTag}] Region: {region}, Language: {language}");
        sb.AppendLine();
        sb.AppendLine($"[{jobTag}] Clarifications:");
        for (int i = 0; i < clarifications.Count; i++)
        {
            sb.AppendLine($"[{jobTag}] Q{i + 1}: {clarifications[i].Question}");
            sb.AppendLine($"[{jobTag}] A{i + 1}: {clarifications[i].Answer}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    // =========================================================
    // Utilities
    // =========================================================
    private static bool IsRole(ChatMessage m, string role)
        => m.Role != null && m.Role.Equals(role, StringComparison.OrdinalIgnoreCase);

    private static void ConfigureOpenAiSseHeaders(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers["Content-Type"] = "text/event-stream";
        httpContext.Response.Headers["Cache-Control"] = "no-cache";
        httpContext.Response.Headers["Connection"] = "keep-alive";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";
    }
}