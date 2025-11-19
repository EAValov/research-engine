using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ResearchApi.Application;
using ResearchApi.Domain;

public sealed class OpenAiChatMessage
{
    public string Role { get; set; } = default!;
    public string Content { get; set; } = default!;
}

public sealed class OpenAiChatRequest
{
    public string Model { get; set; } = "local-deep-research";
    public bool Stream { get; set; } = true;
    public List<OpenAiChatMessage> Messages { get; set; } = new();
}

public static class OpenAiModelEndpoints
{
    private const string ClarificationsBeginMarker = "[LOCAL_DEEP_RESEARCH_CLARIFICATIONS_BEGIN]";
    private const string ClarificationsEndMarker   = "[LOCAL_DEEP_RESEARCH_CLARIFICATIONS_END]";

    public static void MapDeepResearchModel(this WebApplication app)
    {
        app.MapGet("/v1/models", () =>
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
                        id      = "local-deep-research", 
                        @object = "model",
                        created = created,
                        owned_by = "local",
                        description = "Local Deep Research wrapper model"
                    }
                }
            };

            return Results.Json(response);
        });

        app.MapPost(
            "/v1/chat/completions",
            async (
                HttpContext httpContext,
                OpenAiChatRequest request,
                IResearchOrchestrator orchestrator,
                IResearchJobStore jobStore,
                ILlmClient llmClient,
                CancellationToken ct
            ) =>
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

                var serializerOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                async Task SendChunkAsync(object chunk, CancellationToken token)
                {
                    var json = JsonSerializer.Serialize(chunk, serializerOptions);
                    var line = $"data: {json}\n\n";
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await httpContext.Response.Body.WriteAsync(bytes, token);
                    await httpContext.Response.Body.FlushAsync(token);
                }

                async Task WriteDoneAsync(CancellationToken token)
                {
                    var bytes = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
                    await httpContext.Response.Body.WriteAsync(bytes, token);
                    await httpContext.Response.Body.FlushAsync(token);
                }

                async Task WriteErrorChunkAsync(string message, CancellationToken token)
                {
                    var payload = new
                    {
                        id      = requestId,
                        // openai-style streaming object
                        // object = "chat.completion.chunk",
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
                                    content = $"<think>\nError: {message}\n</think>\n"
                                },
                                finish_reason = "error"
                            }
                        }
                    };

                    await SendChunkAsync(payload, token);
                    await WriteDoneAsync(token);
                }

                // ----------------- базовая валидация -------------------

                if (request.Messages is null || request.Messages.Count == 0)
                {
                    await WriteErrorChunkAsync("No messages supplied.", ct);
                    return;
                }

                // --- читаем override через теги [DR_BREADTH=..][DR_DEPTH=..] (если есть) ---
                var (breadthOverride, depthOverride) = ExtractBreadthDepthFromMessages(request.Messages);

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
                    await WriteErrorChunkAsync("No user message provided.", ct);
                    return;
                }

                var firstUser         = userMessages.First();
                var latestUser        = userMessages.Last();
                var initialQuery      = firstUser.m.Content?.Trim() ?? "";
                var latestUserContent = latestUser.m.Content?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(initialQuery))
                {
                    await WriteErrorChunkAsync("Initial user message is empty.", ct);
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
                    var questions = await orchestrator.GenerateFeedbackQueries(initialQuery, 4, configureRequested, ct);

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

                    await SendChunkAsync(chunk, ct);
                    await WriteDoneAsync(ct);
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

                    await SendChunkAsync(repeatQuestionsChunk, ct);
                    await WriteDoneAsync(ct);
                    return;
                }

                // Парсим вопросы из assistantWithClar
                var clarificationQuestions = ExtractQuestionsFromContent(assistantWithClar!.m.Content ?? "");

                // Последний user-пост после блока вопросов — это ответы
                var answerUser = userMessages
                    .Where(um => um.idx > assistantWithClar.idx)
                    .Last();

                var answersText   = answerUser.m.Content?.Trim() ?? "";
                var parsedAnswers = ParseAnswersFromUserText(answersText);

                var clarifications = new List<Clarification>();

                if (clarificationQuestions.Count > 0)
                {
                    for (int i = 0; i < clarificationQuestions.Count; i++)
                    {
                        var q = clarificationQuestions[i];
                        var a = (i < parsedAnswers.Count && !string.IsNullOrWhiteSpace(parsedAnswers[i]))
                            ? parsedAnswers[i]
                            : answersText; // fallback

                        clarifications.Add(new Clarification(q, a));
                    }
                }
                else
                {
                    clarifications.Add(new Clarification("User clarifications", answersText));
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
                        llmClient,
                        ct);
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

                await SendChunkAsync(firstChunk, ct);

                var job = orchestrator.StartJob(
                    initialQuery,
                    clarifications,
                    breadth: breadth,
                    depth: depth);

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
                        currentJob = jobStore.GetJob(job.Id);
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
                            await SendChunkAsync(chunkInternal, ct);
                            break;
                        }

                        var status = currentJob.Status;

                        var events = jobStore.GetEvents(job.Id);
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

                                await SendChunkAsync(evChunk, ct);
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

                currentJob ??= jobStore.GetJob(job.Id);
                if (currentJob != null)
                {
                    var statusStr = currentJob.Status.ToString();
                    var rawReport = currentJob.ReportMarkdown ??
                                 "_No reportMarkdown was generated by the research API._";

                    var report = llmClient.StripThinkBlock(rawReport);

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

                    await SendChunkAsync(finalChunk, ct);
                }

                await WriteDoneAsync(ct);
            });
    }

    /// <summary>
    /// Читает [DR_BREADTH=3][DR_DEPTH=2] из любого сообщения.
    /// Возвращает (null, null), если тегов нет.
    /// </summary>
    private static (int? breadth, int? depth) ExtractBreadthDepthFromMessages(
        IReadOnlyList<OpenAiChatMessage> messages)
    {
        int? breadth = null;
        int? depth   = null;

        var breadthRegex = new Regex(@"\[DR_BREADTH=(\d+)\]", RegexOptions.IgnoreCase);
        var depthRegex   = new Regex(@"\[DR_DEPTH=(\d+)\]", RegexOptions.IgnoreCase);

        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content))
                continue;

            var content = msg.Content;

            var bMatch = breadthRegex.Match(content);
            if (bMatch.Success && int.TryParse(bMatch.Groups[1].Value, out var bVal))
            {
                breadth = bVal;
            }

            var dMatch = depthRegex.Match(content);
            if (dMatch.Success && int.TryParse(dMatch.Groups[1].Value, out var dVal))
            {
                depth = dVal;
            }
        }

        return (breadth, depth);
    }

    /// <summary>
    /// Авто-режим: попросить LLM выбрать breadth/depth по запросу и кларификациям.
    /// LLM должна вернуть чистый JSON: {"breadth":3,"depth":2}
    /// </summary>
    private static async Task<(int breadth, int depth)> AutoSelectBreadthDepthAsync(
        string query,
        IReadOnlyList<Clarification> clarifications,
        ILlmClient llmClient,
        CancellationToken ct)
    {
        const int defaultBreadth = 2;
        const int defaultDepth   = 2;

        var (systemPrompt, userPrompt) = SelectBreadthDepthPromptFactory.Build(query, clarifications);

        var raw = await llmClient.CompleteAsync(systemPrompt, userPrompt, ct);

        if (string.IsNullOrWhiteSpace(raw))
            return (defaultBreadth, defaultDepth);

        raw = llmClient.StripThinkBlock(raw);
        
        // попытка распарсить JSON
        try
        {
            using var doc = JsonDocument.Parse(raw);
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
                raw,
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

    // -------------------------------------------------------------
    //   Парсинг вопросов и ответов
    // -------------------------------------------------------------

    private static List<string> ExtractQuestionsFromContent(string content)
    {
        var result = new List<string>();

        var beginIdx = content.IndexOf(ClarificationsBeginMarker, StringComparison.Ordinal);
        var endIdx   = content.IndexOf(ClarificationsEndMarker,   StringComparison.Ordinal);

        if (beginIdx < 0 || endIdx < 0 || endIdx <= beginIdx)
            return result;

        var start = beginIdx + ClarificationsBeginMarker.Length;
        var len   = endIdx - start;
        var block = content.Substring(start, len);

        var lines = block.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        foreach (var line in lines)
        {
            var stripped = Regex.Replace(line, @"^\d+[\.\)]\s*", "");
            stripped = stripped.Trim();
            if (!string.IsNullOrWhiteSpace(stripped))
                result.Add(stripped);
        }

        return result;
    }

    private static List<string> ParseAnswersFromUserText(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\d+[\.\)\-]\s*"))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                var stripped = Regex.Replace(line, @"^\d+[\.\)\-]\s*", "");
                current.Append(stripped);
            }
            else
            {
                if (current.Length > 0)
                {
                    current.Append(' ');
                    current.Append(line);
                }
                else
                {
                    current.Append(line);
                }
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString().Trim());

        return result;
    }
}