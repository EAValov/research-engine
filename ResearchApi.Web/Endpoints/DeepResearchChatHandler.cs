using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using ResearchApi.Application;
using ResearchApi.Domain;
using ResearchApi.Infrastructure;
using ResearchApi.Prompts;
using Serilog;

public sealed class ChatRequest { public string Model { get; set; } = default!; public bool Stream { get; set; } = true; public List<ChatMessage> Messages { get; set; } = new(); }
public sealed class ChatMessage { public string Role { get; set; } = default!; public string Content { get; set; } = default!; }

public static class DeepResearchChatHandler
{
    private const string ClarificationsBeginMarker = "[LOCAL_DEEP_RESEARCH_CLARIFICATIONS_BEGIN]";
    private const string ClarificationsEndMarker   = "[LOCAL_DEEP_RESEARCH_CLARIFICATIONS_END]";

    public static async Task HandleChatCompletionsAsync(
        HttpContext httpContext,
        ChatRequest request,
        IResearchOrchestrator orchestrator,
        IResearchJobStore jobStore,
        ILlmService llmService,
        CancellationToken ct)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers["Content-Type"]  = "text/event-stream";
        httpContext.Response.Headers["Cache-Control"] = "no-cache";
        httpContext.Response.Headers["Connection"]   = "keep-alive";

        await httpContext.Response.Body.FlushAsync(ct);

        var requestId = $"ldr-{Guid.NewGuid():N}";
        var created   = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var modelName = string.IsNullOrWhiteSpace(request.Model)
            ? "local-deep-research"
            : request.Model;

        var chunkWriter = new SseChunkWriter(httpContext);

        // ----------------- базовая валидация -------------------

        if (request.Messages is null || request.Messages.Count == 0)
        {
            await chunkWriter.WriteErrorChunkAsync("No messages supplied.", requestId, created, modelName, ct);
            return;
        }

        // --- читаем override через теги [DR_BREADTH=..][DR_DEPTH=..] (если есть) ---
        var (breadthOverride, depthOverride) = ResearchProtocolHelper.ExtractBreadthDepthFromMessages(request.Messages);
        
        var (langOverride, regionOverride) = ResearchProtocolHelper.ExtractLanguageRegionFromMessages(request.Messages);

        // --- проверяем /configure ---
        var configureRequested = request.Messages.Any(m =>
            !string.IsNullOrWhiteSpace(m.Content)
            && m.Content.Contains("/configure", StringComparison.OrdinalIgnoreCase));

        // все user-сообщения
        var userMessages = request.Messages
            .Select((m, idx) => new { m, idx })
            .Where(x => x.m.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (userMessages.Count == 0)
        {
            await chunkWriter.WriteErrorChunkAsync("No user message provided.", requestId, created, modelName, ct);
            return;
        }

        var firstUser         = userMessages.First();
        var latestUser        = userMessages.Last();
        var initialQuery      = firstUser.m.Content?.Trim() ?? "";
        var latestUserContent = latestUser.m.Content?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(initialQuery))
        {
            await chunkWriter.WriteErrorChunkAsync("Initial user message is empty.", requestId, created, modelName, ct);
            return;
        }

        // --- debug mode detection ---
        var debugMode = initialQuery.Contains("/debug", StringComparison.OrdinalIgnoreCase);

        // strip the flag from the stored query so research isn't polluted by it
        if (debugMode)
        {
            initialQuery = initialQuery.Replace("/debug", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        // Ищем последнее assistant-сообщение с блоком кларификаций
        var assistantWithClar = request.Messages
            .Select((m, idx) => new { m, idx })
            .LastOrDefault(x =>
                x.m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) &&
                x.m.Content != null &&
                x.m.Content.Contains(ClarificationsBeginMarker, StringComparison.Ordinal));

        var hasClarificationBlock = assistantWithClar != null;

        bool hasUserAfterClar = false;
        if (assistantWithClar != null)
        {
            var clarIdx = assistantWithClar.idx;
            hasUserAfterClar = userMessages.Any(um => um.idx > clarIdx);
        }

        // =========================================================
        // PHASE 1: планирование — задаём уточняющие вопросы
        // =========================================================
        if (!hasClarificationBlock)
        {
            // генерируем список вопросов (если /configure есть — включаем туда вопросы про breadth/depth)
            var questions = await GenerateFeedbackQueries(initialQuery, configureRequested, llmService, ct);

            if (debugMode)
            {
                Log.Logger.Information("[DEBUG MODE]");
                // 1) Let the local LLM answer the questions
                var clarifications_ = await AutoAnswerClarificationsAsync(
                    initialQuery,
                    questions,
                    llmService,
                    ct);
                

                // 2) Jump directly to the “Phase 2” logic, but using our synthetic clarifications
                await RunResearchWithClarificationsAsync(
                    requestId,
                    created,
                    modelName,
                    initialQuery,
                    clarifications_,
                    breadthOverride,
                    depthOverride,
                    langOverride,
                    regionOverride,
                    configureRequested,
                    chunkWriter,
                    orchestrator,
                    jobStore,
                    llmService,
                    httpContext,
                    ct);

                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("To better focus the research, please answer the following clarification questions:");
            sb.AppendLine();
            sb.AppendLine(ClarificationsBeginMarker);

            for (int i = 0; i < questions.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {questions[i]}");
            }

            sb.AppendLine(ClarificationsEndMarker);
            sb.AppendLine();
            sb.AppendLine("You can answer them in a numbered list, for example:");
            sb.AppendLine("1) ...");
            sb.AppendLine("2) ...");
            sb.AppendLine("3) ...");

            var chunk = new
            {
                id      = requestId,
                @object = "chat.completion.chunk",
                created,
                model   = modelName,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new
                        {
                            role    = "assistant",
                            content = sb.ToString()
                        },
                        finish_reason = (string?)null
                    }
                }
            };

            await chunkWriter.WriteChunkAsync(chunk, ct);
            await chunkWriter.WriteDoneAsync(ct);
            return;
        }

        // =========================================================
        // PHASE 2: есть блок вопросов и есть ответ → запускаем ресёрч
        // =========================================================
        if (!hasUserAfterClar)
        {
            // На всякий случай: есть блок с вопросами, но пользователь ещё не ответил.
            var repeatQuestionsChunk = new
            {
                id      = requestId,
                @object = "chat.completion.chunk",
                created,
                model   = modelName,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new
                        {
                            role    = "assistant",
                            content = assistantWithClar!.m.Content
                        },
                        finish_reason = (string?)null
                    }
                }
            };

            await chunkWriter.WriteChunkAsync(repeatQuestionsChunk, ct);
            await chunkWriter.WriteDoneAsync(ct);
            return;
        }

        // Парсим вопросы из assistantWithClar
        var clarificationQuestions = ResearchProtocolHelper.ExtractQuestionsFromContent(assistantWithClar!.m.Content ?? "");

        // Последний user-пост после блока вопросов — это ответы
        var answerUser = userMessages
            .Where(um => um.idx > assistantWithClar.idx)
            .Last();

        var answersText   = answerUser.m.Content?.Trim() ?? "";
        var parsedAnswers = ResearchProtocolHelper.ParseAnswersFromUserText(answersText);

        var clarifications = new List<Clarification>();

        if (clarificationQuestions.Count > 0)
        {
            for (int i = 0; i < clarificationQuestions.Count; i++)
            {
                var q = clarificationQuestions[i];
                var a = (i < parsedAnswers.Count && !string.IsNullOrWhiteSpace(parsedAnswers[i]))
                    ? parsedAnswers[i]
                    : answersText; // fallback

                clarifications.Add(new Clarification {Question = q, Answer = a});
            }
        }
        else
        {
            clarifications.Add(new Clarification {Question = "User clarifications", Answer = answersText });
        }

        // ---------- выбираем breadth/depth ----------

        int breadth;
        int depth;

        if (breadthOverride.HasValue || depthOverride.HasValue)
        {
            // ручной override через теги
            breadth = Math.Clamp(breadthOverride ?? 2, 1, 8);
            depth   = Math.Clamp(depthOverride ?? 2, 1, 4);
        }
        else
        {
            // авто-режим: просим LLM подобрать параметры по запросу и кларификациям
            (breadth, depth) = await AutoSelectBreadthDepthAsync(
                initialQuery,
                clarifications,
                llmService,
                ct);
        }

        // ---------- выбираем breadth/depth ----------

        string language;
        string? region;

        if (!string.IsNullOrWhiteSpace(langOverride) || !string.IsNullOrWhiteSpace(regionOverride))
        {
            language = langOverride ?? "en";
            region = regionOverride;
        }
        else
        {
            (language, region) = await AutoSelectLanguageRegionAsync(initialQuery, clarifications, llmService, ct);
        }

        // ---------- think-header + запуск джобы ----------

        var thinkHeader = new StringBuilder();
        thinkHeader.AppendLine("<think>");
        thinkHeader.AppendLine("Starting local deep research with clarifications.");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine($"User query: \"{initialQuery}\"");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine($"Breadth: {breadth}, Depth: {depth}");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine($"Region: {region}, Language: {language}");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine("Clarifications:");
        for (int i = 0; i < clarifications.Count; i++)
        {
            thinkHeader.AppendLine($"Q{i + 1}: {clarifications[i].Question}");
            thinkHeader.AppendLine($"A{i + 1}: {clarifications[i].Answer}");
            thinkHeader.AppendLine();
        }

        var firstChunk = new
        {
            id      = requestId,
            @object = "chat.completion.chunk",
            created,
            model   = modelName,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new
                    {
                        role    = "assistant",
                        content = thinkHeader.ToString()
                    },
                    finish_reason = (string?)null
                }
            }
        };

        await chunkWriter.WriteChunkAsync(firstChunk, ct);

        var job =  await orchestrator.StartJobAsync(
            initialQuery,
            clarifications,
            breadth: breadth,
            depth: depth,
            language: language,
            region: region,
            ct
        );

        _ = Task.Run(async () =>
        {
            try
            {
                await orchestrator.RunJobAsync(job.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[deep-research-model] Error in RunJobAsync: {ex}");
            }
        });

        var lastEventCount = 0;
        ResearchJob? currentJob = null;

        try
        {
            while (!ct.IsCancellationRequested &&
                   !httpContext.RequestAborted.IsCancellationRequested)
            {
                currentJob = await jobStore.GetJobAsync(job.Id, ct);
                if (currentJob == null)
                {
                    var chunkInternal = new
                    {
                        id      = requestId,
                        @object = "chat.completion.chunk",
                        created,
                        model   = modelName,
                        choices = new[]
                        {
                            new
                            {
                                index = 0,
                                delta = new { content = "\n[internal] Job not found.\n" },
                                finish_reason = (string?)null
                            }
                        }
                    };
                    await chunkWriter.WriteChunkAsync(chunkInternal, ct);
                    break;
                }

                var status = currentJob.Status;

                var events =  await jobStore.GetEventsAsync(job.Id, ct);
                if (events.Count > lastEventCount)
                {
                    var newEvents = events.Skip(lastEventCount).ToList();
                    lastEventCount = events.Count;

                    foreach (var ev in newEvents)
                    {
                        var line = $"{ev.Stage}: {ev.Message}\n";
                        var evChunk = new
                        {
                            id      = requestId,
                            @object = "chat.completion.chunk",
                            created,
                            model   = modelName,
                            choices = new[]
                            {
                                new
                                {
                                    index = 0,
                                    delta = new { content = line },
                                    finish_reason = (string?)null
                                }
                            }
                        };

                        await chunkWriter.WriteChunkAsync(evChunk, ct);
                    }
                }

                if (status is ResearchJobStatus.Completed or ResearchJobStatus.Failed)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // клиент отключился
        }

        // ---------- финальный отчёт ----------

        currentJob ??=  await jobStore.GetJobAsync(job.Id, ct);
        if (currentJob != null)
        {
            var statusStr = currentJob.Status.ToString();
            var rawReport = currentJob.ReportMarkdown ??
                         "_No reportMarkdown was generated by the research API._";

            var report = llmService.StripThinkBlock(rawReport);

            var finalContent =
                $"\n</think>\n\n" +
                $"### Local Deep Research\n\n" +
                $"**Job ID:** `{currentJob.Id}`  \n" +
                $"**Status:** `{statusStr}`  \n" +
                $"**Query:** {currentJob.Query}\n\n" +
                report;

            var finalChunk = new
            {
                id      = requestId,
                @object = "chat.completion.chunk",
                created,
                model   = modelName,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { content = finalContent },
                        finish_reason = (string?)null
                    }
                }
            };

            await chunkWriter.WriteChunkAsync(finalChunk, ct);
        }

        await chunkWriter.WriteDoneAsync(ct);
    }

    /// <summary>
    /// Авто-режим: попросить LLM выбрать breadth/depth по запросу и кларификациям.
    /// LLM должна вернуть чистый JSON: {"breadth":3,"depth":2}
    /// </summary>
    public static async Task<(int breadth, int depth)> AutoSelectBreadthDepthAsync(
        string query,
        IReadOnlyList<Clarification> clarifications,
        ILlmService llmService,
        CancellationToken ct)
    {
        const int defaultBreadth = 2;
        const int defaultDepth   = 2;

        var prompt = SelectBreadthDepthPromptFactory.Build(query, clarifications);

        var raw = await llmService.ChatAsync(prompt, cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(raw.Text))
            return (defaultBreadth, defaultDepth);

        var raw_text = llmService.StripThinkBlock(raw.Text);
        
        // попытка распарсить JSON
        try
        {
            using var doc = JsonDocument.Parse(raw_text);
            var root = doc.RootElement;

            int b = root.TryGetProperty("breadth", out var bProp) && bProp.ValueKind == JsonValueKind.Number
                ? bProp.GetInt32()
                : defaultBreadth;

            int d = root.TryGetProperty("depth", out var dProp) && dProp.ValueKind == JsonValueKind.Number
                ? dProp.GetInt32()
                : defaultDepth;

            b = Math.Clamp(b, 1, 8);
            d = Math.Clamp(d, 1, 4);

            return (b, d);
        }
        catch
        {
            // fallback: выдёргиваем цифры регекспом
            var match = Regex.Match(
                raw_text,
                @"breadth[^0-9]*(\d+).*depth[^0-9]*(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var bVal) &&
                int.TryParse(match.Groups[2].Value, out var dVal))
            {
                var b = Math.Clamp(bVal, 1, 8);
                var d = Math.Clamp(dVal, 1, 4);
                return (b, d);
            }

            return (defaultBreadth, defaultDepth);
        }
    }

    public static async Task<(string language, string? region)> AutoSelectLanguageRegionAsync(
        string query,
        IReadOnlyList<Clarification> clarifications,
        ILlmService llmService,
        CancellationToken ct)
    {
        const string defaultLang = "en";
        const string? defaultRegion = null;

        var prompt = LanguageRegionSelectionPromptFactory.Build(query, clarifications);

        var raw = await llmService.ChatAsync(prompt, cancellationToken: ct);

        var raw_text = llmService.StripThinkBlock(raw.Text);

        try
        {
            Log.Logger.Information("[AutoSelectLanguageRegionAsync] Raw json form LLM: {raw_text}", raw_text);
            
            using var doc = JsonDocument.Parse(raw_text);
            var root = doc.RootElement;

            string lang = root.TryGetProperty("language", out var lProp) && lProp.ValueKind == JsonValueKind.String
                ? lProp.GetString()!
                : defaultLang;

            string? region = root.TryGetProperty("region", out var rProp) && rProp.ValueKind == JsonValueKind.String
                ? rProp.GetString()
                : defaultRegion;

            return (lang, region);
        }
        catch
        {
            return (defaultLang, defaultRegion);
        }
    }

    public static async Task<IReadOnlyList<string>> GenerateFeedbackQueries(string query,  bool includeBreadthDepthQuestions, ILlmService llmService, CancellationToken ct)
    {
        var prompt = FeedbackPromptFactory.Build(query, includeBreadthDepthQuestions);

        var rawResponse = await llmService.ChatAsync(prompt, cancellationToken:ct);

        var json_str = llmService.StripThinkBlock(rawResponse.Text);

        var jsonStart = json_str.IndexOf('{');
        var jsonEnd = json_str.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd >= jsonStart)
        {
            json_str = json_str[jsonStart..(jsonEnd + 1)];
        }

        var plan = JsonSerializer.Deserialize<SerpQueryPlan>(json_str, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var queries = plan?.Queries?
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .ToList() ?? new List<string>();

        return queries;
    }

    public static async Task<List<Clarification>> AutoAnswerClarificationsAsync(
        string query,
        IReadOnlyList<string> questions,
        ILlmService llmService,
        CancellationToken ct)
    {
        // Build a small prompt: system + user
        var sb = new StringBuilder();
        sb.AppendLine("The user asked the following research question:");
        sb.AppendLine(query);
        sb.AppendLine();
        sb.AppendLine("You are filling in *clarification answers* to help another system run deep research.");
        sb.AppendLine("Answer the questions concisely, in a numbered list (1), 2), 3), ...) with one answer per line.");
        sb.AppendLine();
        sb.AppendLine("Questions:");
        for (int i = 0; i < questions.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {questions[i]}");
        }

        var prompt = new Prompt(
            systemPrompt: "You are a helpful assistant that answers clarification questions for research planning.",
            userPrompt: sb.ToString()
        );

        var raw = await llmService.ChatAsync(prompt, cancellationToken:ct);
        var withoutThink = llmService.StripThinkBlock(raw.Text);
        var parsedAnswers = ResearchProtocolHelper.ParseAnswersFromUserText(withoutThink);

        var clarifications = new List<Clarification>();

        for (int i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var a = (i < parsedAnswers.Count && !string.IsNullOrWhiteSpace(parsedAnswers[i]))
                ? parsedAnswers[i]
                : withoutThink; // fallback to full text if parsing failed

            clarifications.Add(new Clarification
            {
                Question = q,
                Answer   = a
            });
        }

        return clarifications;
    }

    private static async Task RunResearchWithClarificationsAsync(
        string requestId,
        long created,
        string modelName,
        string initialQuery,
        List<Clarification> clarifications,
        int? breadthOverride,
        int? depthOverride,
        string? langOverride,
        string? regionOverride,
        bool configureRequested,
        SseChunkWriter chunkWriter,
        IResearchOrchestrator orchestrator,
        IResearchJobStore jobStore,
        ILlmService llmService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // ---------- breadth/depth ----------

        int breadth;
        int depth;

        if (breadthOverride.HasValue || depthOverride.HasValue)
        {
            breadth = Math.Clamp(breadthOverride ?? 2, 1, 8);
            depth   = Math.Clamp(depthOverride   ?? 2, 1, 4);
        }
        else
        {
            (breadth, depth) = await AutoSelectBreadthDepthAsync(
                initialQuery,
                clarifications,
                llmService,
                ct);
        }

        // ---------- language/region ----------

        string language;
        string? region;

        if (!string.IsNullOrWhiteSpace(langOverride) || !string.IsNullOrWhiteSpace(regionOverride))
        {
            language = langOverride ?? "en";
            region   = regionOverride;
        }
        else
        {
            (language, region) = await AutoSelectLanguageRegionAsync(
                initialQuery,
                clarifications,
                llmService,
                ct);
        }

        // ---------- think-header + start job ----------

        var thinkHeader = new StringBuilder();
        thinkHeader.AppendLine("<think>");
        thinkHeader.AppendLine("Starting local deep research with clarifications.");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine($"User query: \"{initialQuery}\"");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine($"Breadth: {breadth}, Depth: {depth}");
        thinkHeader.AppendLine();
        thinkHeader.AppendLine();
        thinkHeader.AppendLine($"Region: {region}, Language: {language}");
        thinkHeader.AppendLine("Clarifications:");
        for (int i = 0; i < clarifications.Count; i++)
        {
            thinkHeader.AppendLine($"Q{i + 1}: {clarifications[i].Question}");
            thinkHeader.AppendLine($"A{i + 1}: {clarifications[i].Answer}");
            thinkHeader.AppendLine();
        }

        var firstChunk = new
        {
            id      = requestId,
            @object = "chat.completion.chunk",
            created,
            model   = modelName,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta = new
                    {
                        role    = "assistant",
                        content = thinkHeader.ToString()
                    },
                    finish_reason = (string?)null
                }
            }
        };

        await chunkWriter.WriteChunkAsync(firstChunk, ct);

        var job = await orchestrator.StartJobAsync(
            initialQuery,
            clarifications,
            breadth: breadth,
            depth: depth,
            language: language,
            region: region,
            ct
        );

        _ = Task.Run(async () =>
        {
            try
            {
                await orchestrator.RunJobAsync(job.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[deep-research-model] Error in RunJobAsync: {ex}");
            }
        });

        var lastEventCount = 0;
        ResearchJob? currentJob = null;

        try
        {
            while (!ct.IsCancellationRequested &&
                !httpContext.RequestAborted.IsCancellationRequested)
            {
                currentJob = await jobStore.GetJobAsync(job.Id, ct);
                if (currentJob == null)
                {
                    var chunkInternal = new
                    {
                        id      = requestId,
                        @object = "chat.completion.chunk",
                        created,
                        model   = modelName,
                        choices = new[]
                        {
                            new
                            {
                                index = 0,
                                delta = new { content = "\n[internal] Job not found.\n" },
                                finish_reason = (string?)null
                            }
                        }
                    };
                    await chunkWriter.WriteChunkAsync(chunkInternal, ct);
                    break;
                }

                var status = currentJob.Status;

                var events = await jobStore.GetEventsAsync(job.Id, ct);
                if (events.Count > lastEventCount)
                {
                    var newEvents = events.Skip(lastEventCount).ToList();
                    lastEventCount = events.Count;

                    foreach (var ev in newEvents)
                    {
                        var line = $"{ev.Stage}: {ev.Message}\n";
                        var evChunk = new
                        {
                            id      = requestId,
                            @object = "chat.completion.chunk",
                            created,
                            model   = modelName,
                            choices = new[]
                            {
                                new
                                {
                                    index = 0,
                                    delta = new { content = line },
                                    finish_reason = (string?)null
                                }
                            }
                        };

                        await chunkWriter.WriteChunkAsync(evChunk, ct);
                    }
                }

                if (status is ResearchJobStatus.Completed or ResearchJobStatus.Failed)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }

        // ---------- final report ----------

        currentJob ??= await jobStore.GetJobAsync(job.Id, ct);
        if (currentJob != null)
        {
            var statusStr = currentJob.Status.ToString();
            var rawReport = currentJob.ReportMarkdown ??
                            "_No reportMarkdown was generated by the research API._";

            var report = llmService.StripThinkBlock(rawReport);

            var finalContent =
                $"\n</think>\n\n" +
                $"### Local Deep Research\n\n" +
                $"**Job ID:** `{currentJob.Id}`  \n" +
                $"**Status:** `{statusStr}`  \n" +
                $"**Query:** {currentJob.Query}\n\n" +
                report;

            var finalChunk = new
            {
                id      = requestId,
                @object = "chat.completion.chunk",
                created,
                model   = modelName,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { content = finalContent },
                        finish_reason = (string?)null
                    }
                }
            };

            await chunkWriter.WriteChunkAsync(finalChunk, ct);
        }

        await chunkWriter.WriteDoneAsync(ct);
    }
}
