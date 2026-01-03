using System.Text;
using System.Text.RegularExpressions;
using ResearchEngine.Domain;

namespace ResearchEngine.Web.OpenAI;

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

        if (cmd.Kind == CommandKind.Help)
        {
            await HandleHelpAsync(requestId, created, modelName, writer, ct);
            await writer.WriteStopAsync(requestId, created, modelName, ct);
            await writer.WriteDoneAsync(ct);
            return;
        }

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

        var report = await BuildReportMarkdownAsync(
            jobId: jobId,
            synthesisId: null,
            jobStore: jobStore,
            ct: ct
        );

        if (string.IsNullOrWhiteSpace(report))
            report = "_No report was generated by the research service._";

        var marker = $"<!-- LOCAL_DEEP_RESEARCH_JOB_ID={jobId} -->";
        var finalContent = marker + "\n\n" + report;

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
        var report = await BuildReportMarkdownAsync(
            jobId: jobId,
            synthesisId: synthesisId,
            jobStore: jobStore,
            ct: ct
        );

        await writer.WriteTextDeltaAsync(
            requestId, created, modelName,
            role: null,
            content: report.Trim(),
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

        var report = await BuildReportMarkdownAsync(
            jobId: jobId,
            synthesisId: null,
            jobStore: jobStore,
            ct: ct
        );

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
    public enum CommandKind
    {
        None = 0,
        Help = 1,
        RegenerateSynthesis = 2
    }

    private sealed record ParsedCommand(
        CommandKind Kind,
        string? Outline,
        string? Instructions);

    private static ParsedCommand ParseCommand(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return new ParsedCommand(CommandKind.None, null, null);

        var text = userText.Trim();

        // /help
        if (text.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/help ", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedCommand(CommandKind.Help, null, null);
        }

        // /regenerate
        if (!text.StartsWith("/regenerate", StringComparison.OrdinalIgnoreCase))
            return new ParsedCommand(CommandKind.None, null, null);

        var tail = text.Substring("/regenerate".Length).Trim();
        if (string.IsNullOrWhiteSpace(tail))
            return new ParsedCommand(CommandKind.RegenerateSynthesis, null, null);

        string? outline = null;
        string? instructions = null;

        var outlineIdx = IndexOfHeader(tail, "outline:");
        var instrIdx   = IndexOfHeader(tail, "instructions:");

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

        static int IndexOfHeader(string t, string header)
            => t.IndexOf(header, StringComparison.OrdinalIgnoreCase);

        static string ExtractBlock(string t, int startIdx, string header, int? endIdx)
        {
            var start = startIdx + header.Length;
            var end = (endIdx.HasValue && endIdx.Value > start) ? endIdx.Value : t.Length;
            return t[start..end].Trim();
        }

        static string? NormalizeOptional(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static async Task HandleHelpAsync(
        string requestId, long created, string modelName,
        OpenAiChatStreamWriter writer,
        CancellationToken ct)
    {
        // Keep it plain markdown; no need for think here.
        var outlineTemplate = """
    {
    "sections": [
        {
        "sectionKey": null,
        "index": 1,
        "title": "Introduction",
        "description": "Define the question, scope, and what will be evaluated.",
        "isConclusion": false
        },
        {
        "sectionKey": null,
        "index": 2,
        "title": "Market overview",
        "description": "Summarize the market structure, demand, and key players.",
        "isConclusion": false
        },
        {
        "sectionKey": null,
        "index": 3,
        "title": "Conclusion",
        "description": "Answer the question and summarize key findings.",
        "isConclusion": true
        }
    ]
    }
    """;

        var text = new StringBuilder();
        text.AppendLine("### Available commands");
        text.AppendLine();
        text.AppendLine("**/help**");
        text.AppendLine("- Show this help message.");
        text.AppendLine();
        text.AppendLine("**/regenerate**");
        text.AppendLine("- Regenerates the latest synthesis for the current job, optionally applying `instructions` and/or an authoritative `outline`.");
        text.AppendLine();
        text.AppendLine("Usage patterns:");
        text.AppendLine();
        text.AppendLine("1) Regenerate with extra instructions:");
        text.AppendLine("```");
        text.AppendLine("/regenerate please remove the “Sources” section");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("2) Regenerate with headers:");
        text.AppendLine("```");
        text.AppendLine("/regenerate");
        text.AppendLine("instructions: Remove the “Sources” section and add a short risk summary.");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("3) Regenerate with strict outline JSON (AUTHORITATIVE):");
        text.AppendLine("```");
        text.AppendLine("/regenerate");
        text.AppendLine("outline:");
        text.AppendLine(outlineTemplate.TrimEnd());
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("Notes for `outline` JSON:");
        text.AppendLine("- `sections` must be a JSON array.");
        text.AppendLine("- `index` should be 1-based and contiguous (it will be normalized defensively).");
        text.AppendLine("- Exactly **one** section must have `isConclusion: true` and it should be the **last** section.");
        text.AppendLine("- `sectionKey`:");
        text.AppendLine("  - `null` => treat as **new section** (a new Guid will be generated).");
        text.AppendLine("  - non-null Guid => stable identity across syntheses (use this to keep/patch sections reliably).");

        await writer.WriteTextDeltaAsync(
            requestId, created, modelName,
            role: "assistant",
            content: text.ToString(),
            finishReason: null,
            ct);
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

    private static readonly Regex LearningCitationRegex =
        new(@"\[lrn:(?<id>[0-9a-fA-F]{32})\]", RegexOptions.Compiled);

    private static async Task<string> BuildReportMarkdownAsync(
        Guid jobId,
        Guid? synthesisId,
        IResearchJobStore jobStore,
        CancellationToken ct)
    {
        Synthesis? synthesis;
        if (synthesisId is not null)
        {
            synthesis = await jobStore.GetSynthesisAsync(synthesisId.Value, ct);
        }
        else
        {
            synthesis = await jobStore.GetLatestSynthesisAsync(jobId, ct);
        }

        if (synthesis is null)
            return "_No synthesis found._";
        
        if (synthesis.Sections.Count == 0)
            return "_No report was generated by the synthesis service._";

        // Build body in deterministic order
        var sb = new StringBuilder();

        foreach (var s in synthesis.Sections.OrderBy(x => x.Index))
        {
            if (string.IsNullOrWhiteSpace(s.ContentMarkdown))
                continue;

            sb.AppendLine($"## {s.Title}");
            sb.AppendLine();
            sb.AppendLine(s.ContentMarkdown.Trim());
            sb.AppendLine();
        }

        var body = sb.ToString().Trim();

        // 2) find all citations used in report
        var usedLearningIds = ExtractLearningIds(body);
        if (usedLearningIds.Count == 0)
        {
            // No citations; just return body (optionally add warning)
            return body;
        }

        // 3) load learnings for job (paged)
        var learningIndex = await LoadLearningsIndexAsync(jobId, jobStore, ct);

        // 4) map used learning IDs -> source URLs
        // Build source index in FIRST-SEEN order within report
        var sourceUrlToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var indexToSourceUrl = new List<string>(); // 0-based list; index = position+1

        foreach (var lrnId in usedLearningIds)
        {
            if (!learningIndex.TryGetValue(lrnId, out var item))
                continue;

            var url = NormalizeUrl(item.SourceReference);
            if (string.IsNullOrWhiteSpace(url))
                url = "about:blank";

            if (!sourceUrlToIndex.ContainsKey(url))
            {
                sourceUrlToIndex[url] = sourceUrlToIndex.Count + 1;
                indexToSourceUrl.Add(url);
            }
        }

        // 5) rewrite [lrn:...] -> [n] (or [n](url))
        var rewritten = RewriteLearningCitationsToNumeric(body, learningIndex, sourceUrlToIndex);

        // 6) append Sources section
        var sourcesSection = BuildSourcesSection(indexToSourceUrl);

        var job = await jobStore.GetJobAsync(jobId, ct);
        var synIdText = synthesis?.ToString() ?? "n/a";

        var finalContent =
            $"**Job ID:** `{jobId}`  \n" +
            $"**Synthesis ID:** `{synIdText}`  \n" +
            (rewritten + sourcesSection).Trim();

        return finalContent;
    }

    private static HashSet<Guid> ExtractLearningIds(string text)
    {
        var set = new HashSet<Guid>();
        foreach (Match m in LearningCitationRegex.Matches(text))
        {
            var hex = m.Groups["id"].Value;
            if (TryParseGuid32Hex(hex, out var guid))
                set.Add(guid);
        }
        return set;
    }

    private static string RewriteLearningCitationsToNumeric(
        string text,
        IReadOnlyDictionary<Guid, LearningListItemDto> learningIndex,
        IReadOnlyDictionary<string, int> sourceUrlToIndex)
    {
        // Replace each [lrn:...] with [n] where n corresponds to the learning's source URL.
        var result = LearningCitationRegex.Replace(text, m =>
        {
            var hex = m.Groups["id"].Value;
            if (!TryParseGuid32Hex(hex, out var lrnId))
                return m.Value;

            if (!learningIndex.TryGetValue(lrnId, out var item))
                return m.Value;

            var url = NormalizeUrl(item.SourceReference);
            if (string.IsNullOrWhiteSpace(url))
                url = "about:blank";

            if (!sourceUrlToIndex.TryGetValue(url, out var idx))
                return m.Value;

             return $"[{idx}]({url})";
        });

        // Optional: fix chained citations accidentally produced, e.g. [1][2] -> [1], [2]
        result = Regex.Replace(result, @"\[(\d+)\]\s*\[(\d+)\]", "[$1], [$2]");

        return result;
    }

    private static string BuildSourcesSection(IReadOnlyList<string> indexToSourceUrl)
    {
        if (indexToSourceUrl.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Sources");
        sb.AppendLine();

        for (int i = 0; i < indexToSourceUrl.Count; i++)
        {
            var idx = i + 1;
            var url = indexToSourceUrl[i];
            sb.AppendLine($"{idx}. {url}");
        }

        return sb.ToString();
    }

    private static string NormalizeUrl(string url)
    {
        url = (url ?? string.Empty).Trim();
        if (url.Length == 0) return url;
        // keep as-is; optionally strip anchors:
        // var hash = url.IndexOf('#'); if (hash > 0) url = url[..hash];
        return url;
    }

    private static bool TryParseGuid32Hex(string hex, out Guid guid)
    {
        return Guid.TryParseExact(hex, "N", out guid);
    }

    private static async Task<Dictionary<Guid, LearningListItemDto>> LoadLearningsIndexAsync(
        Guid jobId,
        IResearchJobStore jobStore,
        CancellationToken ct,
        int pageSize = 500)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);

        var index = new Dictionary<Guid, LearningListItemDto>();

        var skip = 0;
        int total = int.MaxValue;

        while (skip < total)
        {
            ct.ThrowIfCancellationRequested();

            var page = await jobStore.ListLearningsAsync(
                jobId: jobId,
                skip: skip,
                take: pageSize,
                ct: ct);

            if (page is null || page.Items is null || page.Items.Count == 0)
                break;

            total = page.Total;

            foreach (var item in page.Items)
            {
                if (!index.ContainsKey(item.LearningId))
                    index[item.LearningId] = item;
            }

            skip += page.Items.Count;

            if (skip >= total)
                break;
        }

        return index;
    }
}