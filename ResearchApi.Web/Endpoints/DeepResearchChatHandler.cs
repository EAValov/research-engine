using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        IChatModel chatModel,
        IResearchProtocolService protocolService,
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
            var questions = await protocolService.GenerateFeedbackQueriesAsync(initialQuery, configureRequested, ct);

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
            (breadth, depth) = await protocolService.AutoSelectBreadthDepthAsync(
                initialQuery,
                clarifications,
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
            (language, region) = await protocolService.AutoSelectLanguageRegionAsync(initialQuery, clarifications, ct);
        }

        // ---------- think-header + запуск джобы ----------

        var thinkHeader = new StringBuilder();
        thinkHeader.AppendLine(".");
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

            var report = chatModel.StripThinkBlock(rawReport);

            var finalContent =
                $"\n.\n\n" +
                $"### Open Deep Research\n\n" +
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
