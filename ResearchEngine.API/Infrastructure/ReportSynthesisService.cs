using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.AI;
using ResearchEngine.Application;
using ResearchEngine.Domain;
using ResearchEngine.Prompts;
using Serilog.Context;

namespace ResearchEngine.Infrastructure;

public sealed class ReportSynthesisService(
    IChatModel chatModel,
    ITokenizer tokenizer,
    ILearningIntelService learningIntelService,
    IResearchJobRepository jobRepository,
    IResearchSynthesisRepository synthesisRepository,
    IResearchEventRepository eventRepository,
    IBackgroundJobClient backgroundJobs,
    ILogger<ReportSynthesisService> logger
) : IReportSynthesisService
{
    public async Task<Guid> CreateSynthesisAsync(
        Guid jobId,
        Guid? parentSynthesisId,
        string? outline,
        string? instructions,
        CancellationToken ct)
    {
        var synthesis = await synthesisRepository.CreateSynthesisAsync(
            jobId: jobId,
            parentSynthesisId: parentSynthesisId,
            outline: outline,
            instructions: instructions,
            ct: ct);

        return synthesis.Id;
    }

    public string EnqueueSynthesisRun(Guid synthesisId)
    {
        // Schedule *this* method (wrapper), not RunSynthesisAsync with CancellationToken param
        return backgroundJobs.Create(
            Hangfire.Common.Job.FromExpression<ReportSynthesisService>(svc =>
                svc.RunSynthesisBackgroundAsync(synthesisId)),
            new EnqueuedState("synthesis"));
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 300, 1800 })]
    public Task RunSynthesisBackgroundAsync(Guid synthesisId)
        => RunSynthesisAsync(synthesisId, progress: null, ct: CancellationToken.None);
        
    public async Task RunSynthesisAsync(Guid synthesisId, ResearchProgressTracker? progress, CancellationToken ct)
    {
        using var synthesisScope = LogContext.PushProperty("SynthesisId", synthesisId);

        var synthesis = await synthesisRepository.GetSynthesisAsync(synthesisId, ct)
            ?? throw new InvalidOperationException($"Synthesis {synthesisId} not found.");

        using var jobScope = LogContext.PushProperty("JobId", synthesis.JobId);

        // Idempotency / retry safety
        if (synthesis.Status is SynthesisStatus.Completed or SynthesisStatus.Failed)
            return;

        var isManualSynthesisRun = progress is null;
        progress ??= new ResearchProgressTracker(synthesis.JobId, eventRepository, minEmitIntervalMs: 250);

        // Allow a new manual synthesis run after a previous job-level cancel request.
        // Cancellation requested during this run will still be observed by periodic checks below.
        if (isManualSynthesisRun)
            await jobRepository.ClearJobCancelRequestAsync(synthesis.JobId, ct);

        progress.ResetSynthesisMetrics();

        var synTag = $"syn:{synthesis.Id.ToString("N")[..8]}";

        try
        {
            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);
            await synthesisRepository.MarkSynthesisRunningAsync(synthesis.Id, ct);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}] Starting synthesis",
                ct);

            var job = await jobRepository.GetJobAsync(synthesis.JobId, ct)
                ?? throw new InvalidOperationException($"Job {synthesis.JobId} not found.");

            var clarificationsText = FormatClarifications(job.Clarifications ?? Array.Empty<Clarification>());

            // Use persisted values from synthesis row
            var outlineJson = synthesis.Outline;
            var instructions = synthesis.Instructions;

            // Load parent sections (for reuse + key mapping)
            IReadOnlyList<SynthesisSection> parentSections = Array.Empty<SynthesisSection>();
            if (synthesis.ParentSynthesisId is Guid parentId)
            {
                var parent = await synthesisRepository.GetSynthesisAsync(parentId, ct);
                if (parent?.Sections is { Count: > 0 })
                    parentSections = parent.Sections.OrderBy(s => s.Index).ToList();
            }

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][plan] Planning report sections",
                ct);

            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);
            var plans = await PlanSectionsAsync(
                job: job,
                clarificationsText: clarificationsText,
                outlineJson: outlineJson,
                instructions: instructions,
                parentSections: parentSections,
                ct: ct);

            // hard order
            plans = plans.OrderBy(p => p.Index).ToList();

            var (mainPlans, conclusionPlan) = SplitMainAndConclusion(plans);

            progress.SynthesisPlanned(mainPlans.Count);
            progress.SynthesisPlanCompleted();

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}] Planned {mainPlans.Count} sections",
                ct);

            // If we have a parent and NO instructions => reuse section bodies for same keys.
            // (Once you implement “change planning”, this reuse decision can become per-section.)
            var allowReuseFromParent = !string.IsNullOrWhiteSpace(synthesis.ParentSynthesisId?.ToString())
                                       && string.IsNullOrWhiteSpace(instructions)
                                       && parentSections.Count > 0;

            var parentByKey = parentSections
                .GroupBy(s => s.SectionKey)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

            var sectionResults = await GenerateSectionsAsync(
                job: job,
                synthesis: synthesis,
                clarificationsText: clarificationsText,
                mainSections: mainPlans,
                instructions: instructions,
                progress: progress,
                allowReuseFromParent: allowReuseFromParent,
                parentByKey: parentByKey,
                ct: ct);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][conclusion] Writing conclusion",
                ct);

            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);
            var conclusionText = await GenerateConclusionAsync(
                job: job,
                clarificationsText: clarificationsText,
                sectionResults: sectionResults,
                ct: ct);

            progress.SynthesisConclusionCompleted();

            // Build final section rows (including conclusion as a stored section)
            var sectionsToPersist = new List<SynthesisSection>();

            foreach (var sr in sectionResults.OrderBy(r => r.Plan.Index))
            {
                sectionsToPersist.Add(new SynthesisSection
                {
                    Id = Guid.NewGuid(),
                    SynthesisId = synthesis.Id,
                    SectionKey = sr.Plan.SectionKey,
                    Index = sr.Plan.Index - 1, // DB is 0-based
                    Title = sr.Plan.Title,
                    Description = sr.Plan.Description ?? string.Empty,
                    IsConclusion = false,
                    ContentMarkdown = sr.Text ?? string.Empty,
                    Summary = sr.Summary,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            sectionsToPersist.Add(new SynthesisSection
            {
                Id = Guid.NewGuid(),
                SynthesisId = synthesis.Id,
                SectionKey = conclusionPlan.SectionKey,
                Index = conclusionPlan.Index - 1,
                Title = conclusionPlan.Title,
                Description = conclusionPlan.Description ?? string.Empty,
                IsConclusion = true,
                ContentMarkdown = conclusionText ?? string.Empty,
                Summary = null,
                CreatedAt = DateTimeOffset.UtcNow
            });

            // Normalize ordering again defensively
            sectionsToPersist = sectionsToPersist
                .OrderBy(s => s.Index)
                .ToList();

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][finalize] Finalizing citations and sources",
                ct);

            progress.SynthesisFinalized();

            // Persist sections atomically and mark completed
            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);
            await synthesisRepository.CompleteSynthesisAsync(synthesis.Id, sectionsToPersist, ct);

            await progress.SynthesisCompletedAsync(
                synthesis.Id,
                $"[{synTag}] Synthesis completed",
                ct);
        }
        catch (OperationCanceledException oce)
        {
            await synthesisRepository.FailSynthesisAsync(
                synthesisId,
                "Synthesis canceled by user request.",
                CancellationToken.None);

            await eventRepository.AppendEventAsync(
                synthesis.JobId,
                new ResearchEvent(
                    DateTimeOffset.UtcNow,
                    ResearchEventStage.Canceled,
                    $"[{synTag}] Synthesis canceled: {oce.Message}"),
                CancellationToken.None);

            return;
        }
        catch (Exception ex)
        {
            await synthesisRepository.FailSynthesisAsync(synthesisId, ex.Message, ct);

            await eventRepository.AppendEventAsync(
                synthesis.JobId,
                new ResearchEvent(DateTimeOffset.UtcNow, ResearchEventStage.Failed,
                    $"[{synTag}] Synthesis failed: {ex.Message}"),
                ct);

            throw;
        }
    }

    // -----------------------------
    // Planning
    // -----------------------------
    private async Task<List<SectionPlan>> PlanSectionsAsync(
        ResearchJob job,
        string clarificationsText,
        string? outlineJson,
        string? instructions,
        IReadOnlyList<SynthesisSection> parentSections,
        CancellationToken ct)
    {
        // 1) If strict outline JSON provided => authoritative, no LLM planner call.
        if (SynthesisOutline.TryParse(outlineJson, out var outline))
        {
            var fromOutline = OutlineToPlans(outline!);
            
            // ensure keys/index/conclusion normalized
            fromOutline = EnforceIndexOrderAndSingleConclusionAtEnd(fromOutline);
            return fromOutline;
        }

        // 2) Otherwise use LLM planner 
        var planningPrompt = SectionPlanningPromptFactory.BuildPlanningPrompt(
            query: job.Query,
            clarifications: clarificationsText,
            instructions: instructions,
            targetLanguage: job.TargetLanguage);

        var tokens = await tokenizer.TokenizePromptAsync(planningPrompt, ct);
        logger.LogDebug("[ReportSynthesis] Planning prompt tokens: {count}/{max}", tokens.Count, tokens.MaxModelLen);

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var responseFormat = SectionPlanningResponse.JsonResponseSchema(jsonOptions);

        var response = await chatModel.ChatAsync(
            planningPrompt,
            tools: null,
            responseFormat: responseFormat,
            cancellationToken: ct);

        var raw = chatModel.StripThinkBlock(response.Text).Trim();

        SectionPlanningResponse? structured = null;
        try
        {
            structured = JsonSerializer.Deserialize<SectionPlanningResponse>(raw, jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ReportSynthesis] Failed to deserialize SectionPlanningResponse. Raw: {raw}", raw);
        }

        var plans = structured?.ToSectionPlans() is { Count: > 0 } sp
            ? sp.ToList()
            : FallbackSectionPlans().ToList();

        // Normalize order + single conclusion + contiguous indices
        plans = EnforceIndexOrderAndSingleConclusionAtEnd(plans);

        // Assign stable SectionKey based on parent titles when possible (otherwise new Guid)
        plans = AssignSectionKeysFromParentByTitle(plans, parentSections);

        return plans;
    }

    private static List<SectionPlan> OutlineToPlans(SynthesisOutline outline)
    {
        var result = new List<SectionPlan>();

        if (outline.Sections is null || outline.Sections.Count == 0)
            return result;

        foreach (var s in outline.Sections)
        {
            if (string.IsNullOrWhiteSpace(s.Title))
                continue;

            var key = s.SectionKey ?? Guid.NewGuid(); // null => new section identity

            result.Add(new SectionPlan
            {
                SectionKey = key,
                Index = s.Index,
                Title = s.Title.Trim(),
                Description = s.Description?.Trim() ?? string.Empty,
                IsConclusion = s.IsConclusion
            });
        }

        return result;
    }

    private static List<SectionPlan> AssignSectionKeysFromParentByTitle(
        List<SectionPlan> plans,
        IReadOnlyList<SynthesisSection> parentSections)
    {
        if (plans.Count == 0 || parentSections.Count == 0)
        {
            // ensure keys exist
            foreach (var p in plans)
                if (p.SectionKey == Guid.Empty) p.SectionKey = Guid.NewGuid();
            return plans;
        }

        // Simple title matching to reuse existing keys in absence of explicit outline keys
        var parentByTitle = parentSections
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .GroupBy(s => s.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var p in plans)
        {
            if (p.SectionKey != Guid.Empty)
                continue;

            if (parentByTitle.TryGetValue(p.Title.Trim(), out var parent))
            {
                p.SectionKey = parent.SectionKey;
                continue;
            }

            // looser contains match
            var loose = parentSections.FirstOrDefault(ps =>
                !string.IsNullOrWhiteSpace(ps.Title) &&
                (ps.Title.Contains(p.Title, StringComparison.OrdinalIgnoreCase) ||
                 p.Title.Contains(ps.Title, StringComparison.OrdinalIgnoreCase)));

            p.SectionKey = loose?.SectionKey ?? Guid.NewGuid();
        }

        return plans;
    }

    private static List<SectionPlan> EnforceIndexOrderAndSingleConclusionAtEnd(List<SectionPlan> plans)
    {
        if (plans.Count == 0)
            return plans;

        plans = plans
            .Where(p => !string.IsNullOrWhiteSpace(p.Title))
            .ToList();

        if (plans.Count == 0)
            return plans;

        // If indexes are valid, sort by Index; otherwise keep current order.
        var hasValidIndices = plans.All(p => p.Index > 0);
        if (hasValidIndices)
            plans = plans.OrderBy(p => p.Index).ToList();

        // Pick the intended conclusion (if any). If multiple, take the last-by-index (or last in order).
        SectionPlan? intendedConclusion = null;
        var candidates = plans.Where(p => p.IsConclusion).ToList();
        if (candidates.Count > 0)
        {
            intendedConclusion = hasValidIndices
                ? candidates.OrderBy(p => p.Index).Last()
                : candidates.Last();
        }

        // Clear all conclusion flags
        foreach (var p in plans)
            p.IsConclusion = false;

        // If there was an intended conclusion, move it to the end (stable)
        if (intendedConclusion is not null)
        {
            plans.Remove(intendedConclusion);
            plans.Add(intendedConclusion);
        }

        // Ensure keys + contiguous indices
        for (var i = 0; i < plans.Count; i++)
        {
            plans[i].Index = i + 1;

            if (plans[i].SectionKey == Guid.Empty)
                plans[i].SectionKey = Guid.NewGuid();
        }

        // Ensure exactly one conclusion and it is last
        plans[^1].IsConclusion = true;

        return plans;
    }

    private static IReadOnlyList<SectionPlan> FallbackSectionPlans() =>
        new List<SectionPlan>
        {
            new() { SectionKey = Guid.NewGuid(), Index = 1, Title = "Analysis", Description = "Main analysis of the research question.", IsConclusion = false },
            new() { SectionKey = Guid.NewGuid(), Index = 2, Title = "Conclusion", Description = "Final summary and answer to the main question.", IsConclusion = true }
        };

    private static (IReadOnlyList<SectionPlan> Main, SectionPlan Conclusion) SplitMainAndConclusion(IReadOnlyList<SectionPlan> sections)
    {
        if (sections.Count == 0)
        {
            return (
                Array.Empty<SectionPlan>(),
                new SectionPlan { SectionKey = Guid.NewGuid(), Index = 1, Title = "Conclusion", Description = "", IsConclusion = true }
            );
        }

        var ordered = sections.OrderBy(s => s.Index).ToList();
        var conclusion = ordered.LastOrDefault(s => s.IsConclusion) ?? ordered[^1];

        // Ensure last is conclusion
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].IsConclusion = false;
        ordered[^1].IsConclusion = true;
        conclusion = ordered[^1];

        var main = ordered
            .Where(s => !ReferenceEquals(s, conclusion))
            .OrderBy(s => s.Index)
            .ToList();

        return (main, conclusion);
    }

    // -----------------------------
    // Generate sections
    // -----------------------------
    private async Task<List<SectionResult>> GenerateSectionsAsync(
        ResearchJob job,
        Synthesis synthesis,
        string clarificationsText,
        IReadOnlyList<SectionPlan> mainSections,
        string? instructions,
        ResearchProgressTracker progress,
        bool allowReuseFromParent,
        Dictionary<Guid, SynthesisSection> parentByKey,
        CancellationToken ct)
    {
        var results = new List<SectionResult>();
        var synTag = $"syn:{synthesis.Id.ToString("N")[..8]}";

        var ordered = mainSections.OrderBy(s => s.Index).ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);

            var section = ordered[i];
            progress.SynthesisSectionStarted(section.Title);

            // Fast reuse path (only when instructions are empty)
            if (allowReuseFromParent &&
                parentByKey.TryGetValue(section.SectionKey, out var prev) &&
                !string.IsNullOrWhiteSpace(prev.ContentMarkdown))
            {
                results.Add(new SectionResult { Plan = section, Text = prev.ContentMarkdown, Summary = prev.Summary });

                progress.SynthesisSectionWritten(repaired: false);

                continue;
            }

            var prompt = SectionWritingPromptFactory.BuildSectionPrompt(
                query: job.Query,
                clarifications: clarificationsText,
                targetLanguage: job.TargetLanguage ?? "en",
                section: section,
                instructions: instructions);

            var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
            logger.LogDebug("[ReportSynthesis] Section '{title}' prompt tokens: {count}/{max}",
                section.Title, tokens.Count, tokens.MaxModelLen);

            var toolHandler = new SynthesisToolHandler(
                learningIntelService,
                synthesis.Id,
                job.Region,
                job.TargetLanguage);

            var tool = chatModel.CreateTool(toolHandler.HandleGetSimilarLearningsAsync, "get_similar_learnings");

            var response = await chatModel.ChatAsync(
                prompt,
                tools: [tool],
                cancellationToken: ct);

            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);

            var text = chatModel.StripThinkBlock(response.Text).Trim();

            results.Add(new SectionResult { Plan = section, Text = text });
            progress.SynthesisSectionWritten(repaired: false);

            await progress.InfoSynthesisAsync(
                ResearchEventStage.Summarizing,
                $"[{synTag}][section {section.Index}/{ordered.Count}] Summarizing '{section.Title}'",
                ct);

            await ThrowIfJobCanceledAsync(synthesis.JobId, ct);
            results[^1].Summary = await GenerateSectionSummaryAsync(job, results[^1], ct);
            progress.SynthesisSectionSummarized();
        }

        return results.OrderBy(r => r.Plan.Index).ToList();
    }

    private async Task<string> GenerateSectionSummaryAsync(
        ResearchJob job,
        SectionResult section,
        CancellationToken ct)
    {
        var targetLang = job.TargetLanguage ?? "en";

        var prompt = SectionSummaryPromptFactory.BuildSummaryPrompt(
            section.Plan.Title,
            section.Text,
            targetLang);

        var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Section summary '{title}' prompt tokens: {count}/{max}",
            section.Plan.Title, tokens.Count, tokens.MaxModelLen);

        var response = await chatModel.ChatAsync(prompt, tools: null, cancellationToken: ct);
        return chatModel.StripThinkBlock(response.Text).Trim();
    }

    private async Task<string> GenerateConclusionAsync(
        ResearchJob job,
        string clarificationsText,
        IReadOnlyList<SectionResult> sectionResults,
        CancellationToken ct)
    {
        var targetLang = job.TargetLanguage ?? "en";

        var prompt = ConclusionPromptFactory.BuildConclusionPrompt(
            job.Query,
            clarificationsText,
            targetLang,
            sectionResults.OrderBy(s => s.Plan.Index).ToList());

        var tokens = await tokenizer.TokenizePromptAsync(prompt, ct);
        logger.LogDebug("[ReportSynthesis] Conclusion prompt tokens: {count}/{max}", tokens.Count, tokens.MaxModelLen);

        var response = await chatModel.ChatAsync(prompt, tools: null, cancellationToken: ct);
        return chatModel.StripThinkBlock(response.Text).Trim();
    }

    private static string FormatClarifications(IEnumerable<Clarification> clarifications)
    {
        var list = clarifications.ToList();
        if (list.Count == 0)
            return "No additional clarifications were provided.";

        var sb = new StringBuilder();
        sb.AppendLine("Additional context from user clarifications:");
        foreach (var c in list)
        {
            sb.AppendLine($"Q: {c.Question}");
            sb.AppendLine($"A: {c.Answer}");
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    private async Task ThrowIfJobCanceledAsync(Guid jobId, CancellationToken ct)
    {
        if (await jobRepository.IsJobCancelRequestedAsync(jobId, ct))
            throw new OperationCanceledException($"Job {jobId} canceled.");
    }

    private sealed class SectionPlanningResponse
    {
        [Description("Ordered list of planned sections for the report.")]
        public required List<SectionPlanItem> Sections { get; init; }

        public static ChatResponseFormat JsonResponseSchema(JsonSerializerOptions? jsonSerializerOptions = default)
        {
            var jsonElement = AIJsonUtilities.CreateJsonSchema(
                typeof(SectionPlanningResponse),
                description: "Structured section planning result for a research report",
                serializerOptions: jsonSerializerOptions);

            return new ChatResponseFormatJson(jsonElement);
        }

        public IReadOnlyList<SectionPlan> ToSectionPlans()
        {
            var result = new List<SectionPlan>();

            if (Sections is null || Sections.Count == 0)
                return result;

            foreach (var item in Sections)
            {
                if (item.Index <= 0) continue;
                if (string.IsNullOrWhiteSpace(item.Title)) continue;

                result.Add(new SectionPlan
                {
                    SectionKey  = Guid.NewGuid(),
                    Index       = item.Index,
                    Title       = item.Title.Trim(),
                    Description = item.Description?.Trim() ?? string.Empty,
                    IsConclusion = item.IsConclusion
                });
            }

            // Enforce deterministic order and conclusion constraints
            result = result
                .OrderBy(s => s.Index)
                .ToList();

            result = EnforceSingleConclusionAtEndByIndex(result);

            return result;
        }

        private static List<SectionPlan> EnforceSingleConclusionAtEndByIndex(List<SectionPlan> plans)
        {
            if (plans.Count == 0)
                return plans;

            // Clear all
            foreach (var p in plans)
                p.IsConclusion = false;

            // Ensure LAST by Index is conclusion
            plans[^1].IsConclusion = true;

            // Re-number defensively to be contiguous 1..N (optional but helpful)
            for (var i = 0; i < plans.Count; i++)
                plans[i].Index = i + 1;

            return plans;
        }
    }
    private sealed class SectionPlanItem
    {
        [Description("1-based section order index. Must be unique and increasing.")]
        public required int Index { get; init; }     

        [Description("Short, informative title of the report section.")]
        public required string Title { get; init; }

        [Description("One or two sentences describing what this section should cover.")]
        public required string Description { get; init; }

        [Description("True only for the LAST section (conclusion).")]
        public required bool IsConclusion { get; init; }
    }
}
